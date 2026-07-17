using System.Net;

namespace Atlas.Api.Email;

internal sealed record TransactionalEmail(string Subject, string TextBody, string HtmlBody);

internal static class TransactionalEmailTemplates
{
    private const string BrandName = "Reqara";
    internal const string DefaultLogoUrl = "https://api.reqara.com/brand/reqara-email-logo.png";

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

    public static TransactionalEmail EmailVerification(
        string recipientFirstName,
        string organizationName,
        string verificationLink,
        int expiresInMinutes)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeOrganization = CleanName(organizationName, "your organization");
        var url = verificationLink.Trim();
        var expiresText = FormatDuration(expiresInMinutes);

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"Verify your email address to finish setting up Reqara for {safeOrganization}.\n\n" +
            "Verify your email:\n" +
            $"{url}\n\n" +
            $"This link expires in {expiresText}.\n\n" +
            "You can sign in before verifying, but sending live checklist requests and creating API keys requires a verified email.\n\n" +
            "If you did not create this account, you can safely ignore this email.";

        var html = BuildLayout(
            "Verify your email",
            $"Hi {Html(safeFirstName)},",
            $"Verify your email address to finish setting up Reqara for <strong>{Html(safeOrganization)}</strong>.",
            "Verify email",
            url,
            new[]
            {
                $"This link expires in {Html(expiresText)}.",
                "You can sign in before verifying.",
                "Sending live checklist requests and creating API keys requires a verified email.",
                $"Fallback link: <a href=\"{HtmlAttribute(url)}\" style=\"color:#2563eb;word-break:break-all\">{Html(url)}</a>"
            },
            "If you did not create this account, you can safely ignore this email.");

        return new TransactionalEmail("Verify your Reqara email", text, html);
    }

    public static TransactionalEmail PasswordReset(
        string recipientFirstName,
        string resetLink,
        int expiresInMinutes)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var url = resetLink.Trim();
        var expiresText = FormatDuration(expiresInMinutes);

        var text =
            $"Hi {safeFirstName},\n\n" +
            "We received a request to reset your Reqara password.\n\n" +
            "Reset your password:\n" +
            $"{url}\n\n" +
            $"This link expires in {expiresText}. If you did not request this, you can safely ignore this email.";

        var html = BuildLayout(
            "Reset your password",
            $"Hi {Html(safeFirstName)},",
            "We received a request to reset your Reqara password.",
            "Reset password",
            url,
            new[]
            {
                $"This link expires in {Html(expiresText)}.",
                $"Fallback link: <a href=\"{HtmlAttribute(url)}\" style=\"color:#2563eb;word-break:break-all\">{Html(url)}</a>"
            },
            "If you did not request this, you can safely ignore this email.");

        return new TransactionalEmail("Reset your Reqara password", text, html);
    }

    public static TransactionalEmail PublicContact(
        string senderName,
        string senderEmail,
        string topic,
        string message)
    {
        var safeName = CleanName(senderName, "Website visitor");
        var safeEmail = CleanName(senderEmail, "unknown");
        var safeTopic = CleanName(topic, "General");
        var safeMessage = CleanName(message, "No message provided.");

        var text =
            "New Reqara website contact message.\n\n" +
            $"Name: {safeName}\n" +
            $"Email: {safeEmail}\n" +
            $"Topic: {safeTopic}\n\n" +
            $"{safeMessage}";

        var html = BuildLayout(
            "Website contact",
            "New Reqara website contact message.",
            $"<strong>{Html(safeName)}</strong> submitted a contact request.",
            null,
            null,
            new[]
            {
                $"<strong>Email:</strong> {Html(safeEmail)}",
                $"<strong>Topic:</strong> {Html(safeTopic)}",
                $"<strong>Message:</strong><br><span style=\"white-space:pre-line\">{Html(safeMessage)}</span>"
            },
            "Reply directly to this email to reach the sender.");

        return new TransactionalEmail($"Reqara contact: {safeTopic}", text, html);
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

    public static TransactionalEmail ChecklistCancelled(
        string recipientFirstName,
        string senderFullName,
        string organizationName,
        string checklistTitle,
        string? reason)
    {
        var safeFirstName = CleanName(recipientFirstName, "there");
        var safeSender = CleanName(senderFullName, "your sender");
        var safeOrganization = CleanName(organizationName, "the sender");
        var safeTitle = CleanName(checklistTitle, "Checklist request");
        var safeReason = CleanName(reason, string.Empty);
        var reasonText = string.IsNullOrWhiteSpace(safeReason)
            ? string.Empty
            : $"\nReason: {safeReason}\n";
        var reasonDetail = string.IsNullOrWhiteSpace(safeReason)
            ? Array.Empty<string>()
            : new[] { $"<strong>Reason:</strong> {Html(safeReason)}" };

        var text =
            $"Hi {safeFirstName},\n\n" +
            $"{safeSender} at {safeOrganization} has cancelled this checklist request:\n\n" +
            $"{safeTitle}\n" +
            reasonText +
            "\nNo further action is needed.\n\n" +
            $"If you have questions, reply to this email to reach {safeSender} directly.";

        var details = new[] { $"<strong>Request:</strong> {Html(safeTitle)}" }
            .Concat(reasonDetail)
            .ToArray();

        var html = BuildLayout(
            "Checklist cancelled",
            $"Hi {Html(safeFirstName)},",
            $"{Html(safeSender)} at {Html(safeOrganization)} has cancelled this checklist request. No further action is needed.",
            null,
            null,
            details,
            $"If you have questions, reply to this email to reach {Html(safeSender)} directly.");

        return new TransactionalEmail($"{safeOrganization} cancelled this checklist request", text, html);
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

    public static TransactionalEmail RecipientContactOrganization(
        string recipientName,
        string recipientEmail,
        string organizationName,
        string checklistTitle,
        string publicReference,
        string? receiptReference,
        string subject,
        string message)
    {
        var safeRecipient = CleanName(recipientName, "Recipient");
        var safeEmail = CleanName(recipientEmail, "unknown");
        var safeOrganization = CleanName(organizationName, "the organization");
        var safeTitle = CleanName(checklistTitle, "Checklist request");
        var safePublicReference = CleanName(publicReference, "Not set");
        var safeReceiptReference = CleanName(receiptReference, string.Empty);
        var safeSubject = CleanName(subject, "Question about checklist submission");
        var safeMessage = CleanName(message, "No message provided.");

        var referenceLine = string.IsNullOrWhiteSpace(safeReceiptReference)
            ? $"Checklist reference: {safePublicReference}\n"
            : $"Checklist reference: {safePublicReference}\nReceipt reference: {safeReceiptReference}\n";

        var text =
            $"A recipient sent a message to {safeOrganization} through Reqara.\n\n" +
            $"Recipient: {safeRecipient}\n" +
            $"Email: {safeEmail}\n" +
            $"Checklist: {safeTitle}\n" +
            referenceLine +
            $"Subject: {safeSubject}\n\n" +
            $"{safeMessage}\n\n" +
            "Reply directly to this email to reach the recipient.";

        var details = new List<string>
        {
            $"<strong>Recipient:</strong> {Html(safeRecipient)}",
            $"<strong>Email:</strong> {Html(safeEmail)}",
            $"<strong>Checklist:</strong> {Html(safeTitle)}",
            $"<strong>Checklist reference:</strong> {Html(safePublicReference)}"
        };
        if (!string.IsNullOrWhiteSpace(safeReceiptReference))
        {
            details.Add($"<strong>Receipt reference:</strong> {Html(safeReceiptReference)}");
        }

        details.Add($"<strong>Subject:</strong> {Html(safeSubject)}");
        details.Add($"<strong>Message:</strong><br><span style=\"white-space:pre-line\">{Html(safeMessage)}</span>");

        var html = BuildLayout(
            "Recipient message",
            $"A recipient sent a message to {Html(safeOrganization)} through Reqara.",
            $"<strong>{Html(safeRecipient)}</strong> has a question about a checklist submission.",
            null,
            null,
            details,
            "Reply directly to this email to reach the recipient. Reqara did not expose your staff email in the recipient portal.");

        return new TransactionalEmail($"Recipient question: {safeSubject}", text, html);
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
                          <table role="presentation" cellspacing="0" cellpadding="0" style="margin:0 0 12px 0">
                            <tr>
                              <td style="vertical-align:middle;padding:0 10px 0 0">
                                <img src="{HtmlAttribute(DefaultLogoUrl)}" width="36" height="36" alt="{HtmlAttribute(BrandName)}" style="display:block;border:0;outline:none;text-decoration:none;width:36px;height:36px">
                              </td>
                              <td style="vertical-align:middle;font-size:14px;font-weight:800;letter-spacing:.04em;color:#2563eb;text-transform:uppercase">{Html(BrandName)}</td>
                            </tr>
                          </table>
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

    private static string FormatDuration(int minutes)
    {
        return minutes % 60 == 0 && minutes >= 60
            ? $"{minutes / 60} hour{(minutes == 60 ? string.Empty : "s")}"
            : $"{minutes} minute{(minutes == 1 ? string.Empty : "s")}";
    }
}

internal enum ChecklistEmailTemplateKind
{
    Invitation,
    Reminder,
    Overdue
}
