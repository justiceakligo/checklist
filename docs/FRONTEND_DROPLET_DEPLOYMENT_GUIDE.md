# Reqara Frontend Droplet Deployment Guide

This guide explains how to deploy the Reqara frontend web app to the same DigitalOcean droplet that currently hosts the backend API.

Current backend API:

```text
API origin: https://api.reqara.com
API health: https://api.reqara.com/v1/health
API service: atlas-api
API app folder: /opt/atlas-api
API local URL: http://127.0.0.1:5191
```

Target frontend:

```text
Frontend origin: https://reqara.com
Optional www origin: https://www.reqara.com
Frontend app folder: /opt/reqara-web
Frontend service: reqara-web
Frontend local URL: http://127.0.0.1:5192
```

## Deployment Model

The droplet uses Cloudflare Tunnel, not nginx, for public traffic.

The frontend should:

- Build to static files locally or in CI, for example `dist/`.
- Be uploaded to `/opt/reqara-web/releases/<timestamp>`.
- Be served by a local-only Node static server on `127.0.0.1:5192`.
- Be exposed publicly through the existing Cloudflare Tunnel.

Do not open port `5192` publicly. Cloudflare Tunnel should be the only public path.

## Current Droplet Facts

```text
Droplet IP: 159.89.126.21
SSH deploy user: chainroot
SSH key: %USERPROFILE%\.ssh\ryvepool_do
Cloudflare tunnel name: chainroot-api-prod
Cloudflare tunnel ID: cef64064-9bee-415b-ba02-8dd33b5dd0c6
Cloudflare config: /etc/cloudflared/config.yml
Existing API ingress: api.reqara.com -> http://127.0.0.1:5191
Existing Origria ingress: api.origria.info -> http://127.0.0.1:5000
```

SSH:

```powershell
ssh -i $env:USERPROFILE\.ssh\ryvepool_do chainroot@159.89.126.21
```

Root/admin SSH for systemd and Cloudflare config:

```powershell
ssh -i $env:USERPROFILE\.ssh\ryvepool_do root@159.89.126.21
```

## Frontend Build Requirements

The frontend must use the deployed API origin:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

Browser API calls that use cookie auth must include credentials:

```ts
const API_BASE_URL = import.meta.env.VITE_ATLAS_API_BASE_URL ?? "https://api.reqara.com";

await fetch(`${API_BASE_URL}/v1/me`, {
  method: "GET",
  credentials: "include",
  headers: {
    "Accept": "application/json"
  }
});
```

Recipient links:

- The backend stores `app.baseUrl` in the database.
- Today it is `https://reqara.com`.
- After the frontend is live on `https://reqara.com`, update `app.baseUrl` to `https://reqara.com`.
- Email links will then look like `https://reqara.com/c/{token}`.
- The frontend route `/c/{token}` should call `GET https://api.reqara.com/c/{token}` to resolve the token and set the recipient session cookie.

## Step 1: Build The Frontend

From the frontend repository on the build machine:

```powershell
$env:VITE_ATLAS_API_BASE_URL="https://api.reqara.com"
npm ci
npm run build
```

Expected output for Vite/React apps:

```text
dist/
```

If the frontend uses a different framework:

- Vite/React: deploy `dist/`.
- Create React App: deploy `build/`.
- Next.js static export: deploy `out/`.
- Next.js SSR: do not use this static guide directly. Use a Node SSR systemd service on a separate port.

## Step 2: Package The Build

For a Vite app:

```powershell
tar.exe -czf C:\tmp\reqara-web-dist.tgz -C dist .
```

For Create React App:

```powershell
tar.exe -czf C:\tmp\reqara-web-dist.tgz -C build .
```

For Next static export:

```powershell
tar.exe -czf C:\tmp\reqara-web-dist.tgz -C out .
```

## Step 3: Prepare The Droplet

Run once as `root`:

```bash
apt-get update
apt-get install -y nodejs
mkdir -p /opt/reqara-web/releases
chown -R chainroot:chainroot /opt/reqara-web
chmod 750 /opt/reqara-web
```

Verify Node is available:

```bash
node -v
```

## Step 4: Install The Static Server

Create `/opt/reqara-web/server.mjs` as `root`:

```bash
cat > /opt/reqara-web/server.mjs <<'EOF'
import { createServer } from "node:http";
import { createReadStream, statSync, existsSync } from "node:fs";
import { extname, join, normalize } from "node:path";

const port = Number(process.env.PORT ?? "5192");
const root = process.env.WEB_ROOT ?? "/opt/reqara-web/current";

const types = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".webp": "image/webp",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".txt": "text/plain; charset=utf-8"
};

function safePath(urlPath) {
  const decoded = decodeURIComponent(urlPath.split("?")[0] ?? "/");
  const cleaned = normalize(decoded).replace(/^(\.\.[/\\])+/, "");
  return join(root, cleaned === "/" ? "index.html" : cleaned);
}

function send(res, status, body, contentType = "text/plain; charset=utf-8") {
  res.writeHead(status, {
    "Content-Type": contentType,
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
    "X-Frame-Options": "DENY"
  });
  res.end(body);
}

createServer((req, res) => {
  if (req.method !== "GET" && req.method !== "HEAD") {
    send(res, 405, "Method Not Allowed");
    return;
  }

  let path = safePath(req.url ?? "/");
  if (!existsSync(path)) {
    path = join(root, "index.html");
  }

  try {
    const file = statSync(path);
    if (!file.isFile()) {
      send(res, 404, "Not Found");
      return;
    }

    const ext = extname(path);
    const headers = {
      "Content-Type": types[ext] ?? "application/octet-stream",
      "Content-Length": file.size,
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "X-Frame-Options": "DENY"
    };

    if (path.endsWith("index.html")) {
      headers["Cache-Control"] = "no-store";
    } else {
      headers["Cache-Control"] = "public, max-age=31536000, immutable";
    }

    res.writeHead(200, headers);
    if (req.method === "HEAD") {
      res.end();
      return;
    }
    createReadStream(path).pipe(res);
  } catch {
    send(res, 500, "Internal Server Error");
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`Reqara frontend listening on http://127.0.0.1:${port}, root=${root}`);
});
EOF

chown chainroot:chainroot /opt/reqara-web/server.mjs
chmod 640 /opt/reqara-web/server.mjs
```

Why this server:

- It binds to localhost only.
- It supports SPA fallback for routes like `/dashboard` and `/c/{token}`.
- It sets `Referrer-Policy: no-referrer`, which is important for recipient token privacy.
- It avoids adding nginx to a droplet that is already standardized on Cloudflare Tunnel.

## Step 5: Create The systemd Service

Create `/etc/systemd/system/reqara-web.service`:

```bash
cat > /etc/systemd/system/reqara-web.service <<'EOF'
[Unit]
Description=Reqara Frontend Web
After=network.target

[Service]
WorkingDirectory=/opt/reqara-web
ExecStart=/usr/bin/node /opt/reqara-web/server.mjs
User=chainroot
Group=chainroot
Environment=NODE_ENV=production
Environment=PORT=5192
Environment=WEB_ROOT=/opt/reqara-web/current
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable reqara-web
```

Do not start it until a release has been uploaded and `/opt/reqara-web/current` exists.

## Step 6: Upload A Release

From Windows:

```powershell
scp -i $env:USERPROFILE\.ssh\ryvepool_do C:\tmp\reqara-web-dist.tgz chainroot@159.89.126.21:/tmp/reqara-web-dist.tgz
```

On the droplet as `chainroot`:

```bash
RELEASE="$(date +%Y%m%d%H%M%S)"
mkdir -p "/opt/reqara-web/releases/$RELEASE"
tar -xzf /tmp/reqara-web-dist.tgz -C "/opt/reqara-web/releases/$RELEASE"
ln -sfn "/opt/reqara-web/releases/$RELEASE" /opt/reqara-web/current
rm -f /tmp/reqara-web-dist.tgz
```

Start or restart the service as `root`:

```bash
systemctl restart reqara-web
systemctl status reqara-web --no-pager
```

Verify local-only frontend:

```bash
curl -I http://127.0.0.1:5192/
curl -I http://127.0.0.1:5192/c/test-token
```

Expected:

```text
HTTP/1.1 200 OK
Referrer-Policy: no-referrer
```

## Step 7: Add Cloudflare Tunnel Ingress

Back up the tunnel config:

```bash
cp /etc/cloudflared/config.yml "/etc/cloudflared/config.yml.bak-web-$(date +%Y%m%d%H%M%S)"
```

Edit `/etc/cloudflared/config.yml` so it includes `reqara.com` and `www.reqara.com` before the final `http_status:404` rule:

```yaml
tunnel: cef64064-9bee-415b-ba02-8dd33b5dd0c6
credentials-file: /etc/cloudflared/cef64064-9bee-415b-ba02-8dd33b5dd0c6.json

ingress:
  - hostname: api.origria.info
    service: http://127.0.0.1:5000
  - hostname: api.reqara.com
    service: http://127.0.0.1:5191
  - hostname: reqara.com
    service: http://127.0.0.1:5192
  - hostname: www.reqara.com
    service: http://127.0.0.1:5192
  - service: http_status:404
```

Validate and restart:

```bash
cloudflared tunnel ingress validate /etc/cloudflared/config.yml
systemctl restart cloudflared
systemctl status cloudflared --no-pager
```

Check logs:

```bash
journalctl -u cloudflared -n 80 --no-pager
```

## Step 8: Update Cloudflare DNS

The `reqara.com` zone is already authorized for the tunnel.

Create or replace the public hostname routes:

```bash
cloudflared tunnel route dns --overwrite-dns chainroot-api-prod reqara.com
cloudflared tunnel route dns --overwrite-dns chainroot-api-prod www.reqara.com
```

In the Cloudflare dashboard, the DNS page should show:

```text
reqara.com      Tunnel    chainroot-api-prod    Proxied
www.reqara.com  Tunnel    chainroot-api-prod    Proxied
api.reqara.com  Tunnel    chainroot-api-prod    Proxied
```

Important:

- Replace the existing `reqara.com` A record that points to parking or another IP.
- Replace the existing `www.reqara.com` CNAME that points to `parkingpage.namecheap.com`.
- Leave MX records as DNS only.
- Leave SPF/TXT records as DNS only unless the email provider instructs otherwise.

## Step 9: Verify Public Frontend

From any machine:

```powershell
curl.exe -I https://reqara.com/
curl.exe -I https://www.reqara.com/
curl.exe -I https://reqara.com/c/test-token
curl.exe -s https://api.reqara.com/v1/health
```

Expected API health:

```json
{"status":"ok","service":"atlas-api"}
```

Expected frontend response:

```text
HTTP/2 200
server: cloudflare
referrer-policy: no-referrer
```

If a local browser shows DNS errors but public DNS works, flush local DNS or change the workstation DNS to Cloudflare/Google DNS. This is a client resolver issue, not a droplet issue.

## Step 10: Update Backend App BaseUrl

After `https://reqara.com` is confirmed live, update the backend database setting so new email links use the main domain.

On the droplet:

```bash
PGPASSWORD='<POSTGRES_PASSWORD>' psql -h localhost -U postgres -d atlaschecklist <<'SQL'
UPDATE admin_settings
SET value_json = '"https://reqara.com"',
    updated_at = now(),
    updated_by_user_id = NULL
WHERE organization_id IS NULL
  AND category = 'app'
  AND key = 'baseUrl';

SELECT category, key, value_json
FROM admin_settings
WHERE organization_id IS NULL
  AND category = 'app'
  AND key = 'baseUrl';
SQL
```

This changes newly generated checklist email links from:

```text
https://reqara.com/c/{token}
```

to:

```text
https://reqara.com/c/{token}
```

## Step 11: End-To-End Smoke Test

1. Open `https://reqara.com`.
2. Sign up or log in.
3. Confirm browser requests go to `https://api.reqara.com`.
4. Confirm requests include `credentials: "include"`.
5. Create a test checklist.
6. Confirm the invitation email sender is:

```text
Reqara <requests@reqara.com>
```

7. Open the recipient link.
8. Confirm the frontend loads `/c/{token}` on `https://reqara.com`.
9. Confirm the frontend calls `GET https://api.reqara.com/c/{token}`.
10. Confirm the recipient session cookie is set and checklist loads.

## Rollback

List releases:

```bash
ls -1 /opt/reqara-web/releases
```

Rollback to a previous release:

```bash
ln -sfn /opt/reqara-web/releases/<previous-release> /opt/reqara-web/current
systemctl restart reqara-web
```

If the Cloudflare frontend route is the problem, restore the previous tunnel config:

```bash
cp /etc/cloudflared/config.yml.bak-web-<timestamp> /etc/cloudflared/config.yml
cloudflared tunnel ingress validate /etc/cloudflared/config.yml
systemctl restart cloudflared
```

## Operational Commands

Frontend service:

```bash
systemctl status reqara-web --no-pager
journalctl -u reqara-web -n 120 --no-pager
curl -I http://127.0.0.1:5192/
```

API service:

```bash
systemctl status atlas-api --no-pager
journalctl -u atlas-api -n 120 --no-pager
curl -s http://127.0.0.1:5191/v1/health
```

Cloudflare Tunnel:

```bash
systemctl status cloudflared --no-pager
journalctl -u cloudflared -n 120 --no-pager
cloudflared tunnel info chainroot-api-prod
cloudflared tunnel ingress validate /etc/cloudflared/config.yml
```

Port check:

```bash
ss -tln | grep -E ':(5000|5191|5192) '
```

Expected:

```text
127.0.0.1:5000  Origria API
127.0.0.1:5191  Reqara API
127.0.0.1:5192  Reqara frontend
```

## Optional: Separate Platform Admin Host

If the platform admin is a separate frontend app, deploy it as another isolated service.

Recommended values:

```text
Host: admin.reqara.com
Folder: /opt/reqara-admin
Service: reqara-admin
Local URL: http://127.0.0.1:5193
```

Add Cloudflare ingress:

```yaml
  - hostname: admin.reqara.com
    service: http://127.0.0.1:5193
```

Create DNS route:

```bash
cloudflared tunnel route dns --overwrite-dns chainroot-api-prod admin.reqara.com
```

If admin and customer dashboard live in the same frontend app, this optional service is not needed.

## Production Hardening Later

The backend currently allows any browser origin with credentials so the frontend can integrate quickly.

Before final production hardening:

- Restrict CORS origins to `https://reqara.com`, `https://www.reqara.com`, and the chosen admin origin.
- Keep auth cookies `Secure`.
- Keep recipient pages on `Referrer-Policy: no-referrer`.
- Add a real deploy user for Reqara if the team wants stricter isolation than the shared `chainroot` user.
- Add CI/CD so releases are built reproducibly rather than manually packaged.
- Add monitoring for `reqara-web`, `atlas-api`, `cloudflared`, and PostgreSQL.
