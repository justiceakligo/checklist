using System.Net;

namespace Atlas.Api.Email;

internal sealed record TransactionalEmail(string Subject, string TextBody, string HtmlBody);

internal static class TransactionalEmailTemplates
{
    private const string BrandName = "Reqara";

    public static TransactionalEmail AccountCreated(
        string recipientFirstName,
        string organizationName,
        string appBaseUrl)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeOrganization = CleanName(organizationName, "your organization");
        var url = appBaseUrl.TrimEnd('/');

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"Your Reqara workspace for {safeOrganization} is ready.\n\n" +
            "You can sign in and start creating secure checklists here:\n" +
            $"{url}\n\n" +
            "Use Reqara to collect documents, approvals, and information without asking recipients to create an account.\n\n" +
            "If you did not create this account, you can safely ignore this email.";

        var html = BuildLayout(
            "Your workspace is ready",
            $"Hi {Html(safeFirstName)},",
            $"Your Reqara workspace for <strong>{Html(safeOrganization)}</strong> is ready.",
            "Open Reqara",
            url,
            new[]
            {
                "Create reusable checklist templates.",
                "Send private recipient links with optional access codes.",
                "Track document collection and submissions in one place."
            },
            "If you did not create this account, you can safely ignore this email.");

        return new TransactionalEmail("Welcome to Reqara", text, html);
    }

    public static TransactionalEmail RecipientOtp(
        string organizationName,
        string checklistTitle,
        string code,
        int expiresInMinutes)
    {
        var safeOrganization = CleanName(organizationName, "the sender");
        var safeTitle = CleanName(checklistTitle, "your checklist");

        var text =
            $"Your Reqara access code for \"{safeTitle}\" is {code}.\n\n" +
            $"It expires in {expiresInMinutes} minutes.\n\n" +
            $"Organization: {safeOrganization}\n\n" +
            "If you did not request this code, you can safely ignore this email.";

        var html = BuildLayout(
            "Your access code",
            "Use this code to open your secure checklist.",
            $"Checklist: <strong>{Html(safeTitle)}</strong><br>Organization: {Html(safeOrganization)}",
            null,
            null,
            new[]
            {
                $"<span style=\"font-size:30px;font-weight:800;letter-spacing:7px;color:#111827\">{Html(code)}</span>",
                $"This code expires in {expiresInMinutes} minutes."
            },
            "If you did not request this code, you can safely ignore this email.");

        return new TransactionalEmail("Your Reqara access code", text, html);
    }

    public static TransactionalEmail Checklist(
        string recipientFirstName,
        string senderFullName,
        string organizationName,
        string checklistTitle,
        string dueDate,
        string expiresAt,
        string link,
        ChecklistEmailTemplateKind kind)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeSender = CleanName(senderFullName, "your sender");
        var safeOrganization = CleanName(organizationName, "the sender");
        var safeTitle = CleanName(checklistTitle, "Checklist request");
        var safeDueDate = CleanName(dueDate, "Not set");
        var safeExpiresAt = CleanName(expiresAt, "Not set");
        var url = link.Trim();

        var subject = kind switch
        {
            ChecklistEmailTemplateKind.Reminder => $"Reminder: {safeOrganization} is waiting on a few items",
            ChecklistEmailTemplateKind.Overdue => $"Your checklist from {safeOrganization} is past due",
            _ => $"{safeOrganization} needs a few things from you"
        };

        var intro = kind switch
        {
            ChecklistEmailTemplateKind.Reminder => $"{safeSender} at {safeOrganization} is still waiting on a few items from you.",
            ChecklistEmailTemplateKind.Overdue => $"{safeSender} at {safeOrganization} is waiting on a checklist that is now past due.",
            _ => $"{safeSender} at {safeOrganization} has requested some information and documents from you."
        };

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"{intro}\n\n" +
            $"Request: {safeTitle}\n" +
            $"Due: {safeDueDate}\n\n" +
            "Open your secure checklist:\n" +
            $"{url}\n\n" +
            "You don't need an account. The link is private to you - please don't forward it.\n" +
            $"It expires on {safeExpiresAt}.\n\n" +
            $"If you have questions, reply to this email to reach {safeSender} directly.";

        var html = BuildLayout(
            kind switch
            {
                ChecklistEmailTemplateKind.Reminder => "Reminder",
                ChecklistEmailTemplateKind.Overdue => "Checklist past due",
                _ => "Secure checklist request"
            },
            $"Hi {Html(safeFirstName)},",
            Html(intro),
            "Open your checklist",
            url,
            new[]
            {
                $"<strong>Request:</strong> {Html(safeTitle)}",
                $"<strong>Due:</strong> {Html(safeDueDate)}",
                $"<strong>Expires:</strong> {Html(safeExpiresAt)}",
                $"Fallback link: <a href=\"{HtmlAttribute(url)}\" style=\"color:#2563eb;word-break:break-all\">{Html(url)}</a>"
            },
            "This link is private to you. If you did not expect this email, you can safely ignore it.");

        return new TransactionalEmail(subject, text, html);
    }

    public static TransactionalEmail ReturnLink(
        string recipientFirstName,
        string organizationName,
        string checklistTitle,
        string expiresAt,
        string link)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeOrganization = CleanName(organizationName, "the sender");
        var safeTitle = CleanName(checklistTitle, "your checklist");
        var safeExpiresAt = CleanName(expiresAt, "Not set");
        var url = link.Trim();

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"Here is a fresh private link for \"{safeTitle}\" from {safeOrganization}.\n\n" +
            $"{url}\n\n" +
            $"This link expires on {safeExpiresAt}. Please do not forward it.\n\n" +
            "If you did not request this link, you can safely ignore this email.";

        var html = BuildLayout(
            "Your checklist link",
            $"Hi {Html(safeFirstName)},",
            $"Here is a fresh private link for <strong>{Html(safeTitle)}</strong> from {Html(safeOrganization)}.",
            "Return to checklist",
            url,
            new[]
            {
                $"<strong>Expires:</strong> {Html(safeExpiresAt)}",
                $"Fallback link: <a href=\"{HtmlAttribute(url)}\" style=\"color:#2563eb;word-break:break-all\">{Html(url)}</a>"
            },
            "If you did not request this link, you can safely ignore this email.");

        return new TransactionalEmail("Your Reqara checklist link", text, html);
    }

    public static TransactionalEmail ApiKeyCreatedNotification(
        string recipientFirstName,
        string organizationName,
        string keyName,
        string keyPrefix,
        string? expiresAt,
        string appBaseUrl)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeOrganization = CleanName(organizationName, "your organization");
        var safeKeyName = CleanName(keyName, "API key");
        var safeKeyPrefix = CleanName(keyPrefix, "new key");
        var safeExpiresAt = CleanName(expiresAt, "No expiry set");
        var url = appBaseUrl.TrimEnd('/');

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"A Reqara API key was created for {safeOrganization}.\n\n" +
            $"Key name: {safeKeyName}\n" +
            $"Key prefix: {safeKeyPrefix}\n" +
            $"Expires: {safeExpiresAt}\n\n" +
            "For security, the secret value is not included in email. Ask your platform administrator to share it through an approved secure channel if you are the intended owner.\n\n" +
            $"Developer settings: {url}\n\n" +
            "If you did not expect this, rotate or revoke the key immediately.";

        var html = BuildLayout(
            "API key created",
            $"Hi {Html(safeFirstName)},",
            $"A Reqara API key was created for <strong>{Html(safeOrganization)}</strong>.",
            "Open Reqara",
            url,
            new[]
            {
                $"<strong>Key name:</strong> {Html(safeKeyName)}",
                $"<strong>Key prefix:</strong> {Html(safeKeyPrefix)}",
                $"<strong>Expires:</strong> {Html(safeExpiresAt)}",
                "For security, the secret value is not included in email."
            },
            "If you did not expect this, rotate or revoke the key immediately.");

        return new TransactionalEmail("Reqara API key created", text, html);
    }

    private static string BuildLayout(
        string heading,
        string greeting,
        string body,
        string? ctaText,
        string? ctaUrl,
        IReadOnlyList<string> details,
        string footer)
    {
        var detailItems = string.Join(
            "",
            details.Select(item => $"<li style=\"margin:0 0 8px 0\">{item}</li>"));
        var cta = string.IsNullOrWhiteSpace(ctaText) || string.IsNullOrWhiteSpace(ctaUrl)
            ? string.Empty
            : $"""
              <p style="margin:28px 0 20px 0">
                <a href="{HtmlAttribute(ctaUrl)}" style="display:inline-block;background:#2563eb;color:#ffffff;text-decoration:none;padding:13px 20px;border-radius:6px;font-weight:700">{Html(ctaText)}</a>
              </p>
              """;

        return
            $"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f6f7fb;font-family:Arial,Helvetica,sans-serif;color:#111827;line-height:1.55">
              <div style="display:none;max-height:0;overflow:hidden;color:#f6f7fb">{Html(BrandName)} secure checklist notification</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f6f7fb;padding:28px 12px">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #e5e7eb;border-radius:8px;overflow:hidden">
                      <tr>
                        <td style="padding:22px 24px;border-bottom:1px solid #e5e7eb">
                          <div style="font-size:14px;font-weight:800;letter-spacing:.04em;color:#2563eb;text-transform:uppercase">{Html(BrandName)}</div>
                          <h1 style="margin:12px 0 0 0;font-size:24px;line-height:1.25;color:#111827">{Html(heading)}</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:24px">
                          <p style="margin:0 0 14px 0">{greeting}</p>
                          <p style="margin:0 0 18px 0">{body}</p>
                          {cta}
                          <ul style="padding-left:20px;margin:0 0 22px 0;color:#374151">{detailItems}</ul>
                          <p style="margin:0;color:#6b7280;font-size:13px">{Html(footer)}</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string CleanName(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string HtmlAttribute(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}

internal enum ChecklistEmailTemplateKind
{
    Invitation,
    Reminder,
    Overdue
}
