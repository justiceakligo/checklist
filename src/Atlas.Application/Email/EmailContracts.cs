namespace Atlas.Application.Email;

public sealed record EmailSendResult(string Status, string? MessageId = null, string? Error = null)
{
    public bool Sent => Status.Equals("sent", StringComparison.OrdinalIgnoreCase);
}

public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string? textBody,
        string? htmlBody,
        string fromEmail,
        string? fromName = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null,
        string? replyToEmail = null);
}
