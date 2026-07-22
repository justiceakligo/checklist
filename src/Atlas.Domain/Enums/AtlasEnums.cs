namespace Atlas.Domain.Enums;

public enum OrganizationStatus : short
{
    Active = 1,
    Suspended = 2,
    Closed = 3
}

public enum OrganizationUserRole : short
{
    Owner = 1,
    Admin = 2,
    Member = 3,
    Viewer = 4
}

public enum MembershipStatus : short
{
    Invited = 1,
    Active = 2,
    Disabled = 3
}

public enum OrganizationTeamStatus : short
{
    Active = 1,
    Archived = 2
}

public enum TemplateStatus : short
{
    Draft = 1,
    Published = 2,
    Archived = 3
}

public enum RequirementType : short
{
    Text = 1,
    LongText = 2,
    Number = 3,
    Date = 4,
    Boolean = 5,
    SingleSelect = 6,
    MultiSelect = 7,
    File = 8,
    Signature = 9,
    Email = 10,
    Phone = 11
}

public enum ChecklistActionStatus : short
{
    Draft = 1,
    Sent = 2,
    InProgress = 3,
    Submitted = 4,
    Completed = 5,
    Cancelled = 6,
    Expired = 7
}

public enum ActionRecipientStatus : short
{
    Pending = 1,
    Viewed = 2,
    Started = 3,
    Submitted = 4,
    Cancelled = 5,
    Expired = 6
}

public enum SubmissionStatus : short
{
    Submitted = 1,
    Accepted = 2,
    ChangesRequested = 3
}

public enum FileScanStatus : short
{
    Pending = 1,
    Clean = 2,
    Rejected = 3
}

public enum ReminderType : short
{
    BeforeDue = 1,
    Overdue = 2,
    Manual = 3
}

public enum ReminderStatus : short
{
    Pending = 1,
    Sent = 2,
    Cancelled = 3,
    Failed = 4
}

public enum DeliveryChannel : short
{
    Email = 1,
    Sms = 2
}

public enum NotificationDeliveryStatus : short
{
    Queued = 1,
    Delivered = 2,
    Bounced = 3,
    Failed = 4
}

public enum ActorType : short
{
    User = 1,
    Recipient = 2,
    System = 3,
    Api = 4
}

public enum WebhookEndpointStatus : short
{
    Active = 1,
    Disabled = 2
}

public enum WebhookDeliveryStatus : short
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

public enum AdminSettingScope : short
{
    System = 1,
    Organization = 2
}

public enum PlatformStaffRole : short
{
    Owner = 1,
    Admin = 2,
    Support = 3,
    Finance = 4
}

public enum PlatformStaffStatus : short
{
    Active = 1,
    Disabled = 2
}

public enum OrganizationInterestStatus : short
{
    New = 1,
    Qualified = 2,
    Approved = 3,
    Rejected = 4,
    Archived = 5
}

public enum PlatformRevenueEventType : short
{
    Subscription = 1,
    Usage = 2,
    Credit = 3,
    Refund = 4,
    Adjustment = 5
}

public enum ApiKeyEnvironment : short
{
    Sandbox = 1,
    Production = 2
}

public enum DeveloperAccessStatus : short
{
    SandboxOnly = 1,
    ProductionRequested = 2,
    ProductionApproved = 3,
    ProductionRejected = 4
}

public enum UserAuthTokenPurpose : short
{
    EmailVerification = 1,
    PasswordReset = 2
}

public enum InvestorReportStatus : short
{
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public enum SubmissionPackageStatus : short
{
    Preparing = 1,
    Ready = 2,
    RoutingPending = 3,
    PartiallyDelivered = 4,
    Delivered = 5,
    DeliveryFailed = 6,
    HandoffConfirmed = 7,
    Archived = 8,
    Superseded = 9
}

public enum DestinationType : short
{
    ManualDownload = 1,
    Email = 2,
    Webhook = 3,
    ApiPull = 4,
    CsvExport = 5,
    JsonExport = 6,
    SharePoint = 7,
    OneDrive = 8,
    GoogleDrive = 9,
    Sftp = 10
}

public enum DestinationStatus : short
{
    Active = 1,
    Disabled = 2,
    Invalid = 3
}

public enum RoutingTrigger : short
{
    OnSubmission = 1,
    OnAcceptance = 2,
    OnPackageReady = 3,
    ManualOnly = 4
}

public enum DeliveryJobStatus : short
{
    Queued = 1,
    Preparing = 2,
    Sending = 3,
    Succeeded = 4,
    Failed = 5,
    RetryScheduled = 6,
    Cancelled = 7,
    RequiresAttention = 8
}

public enum DeliveryAttemptStatus : short
{
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

public enum WhatsAppConnectionStatus : short
{
    PendingVerification = 1,
    Active = 2,
    Disabled = 3,
    Invalid = 4,
    Revoked = 5
}

public enum LeadStatus : short
{
    New = 1,
    Assigned = 2,
    Contacted = 3,
    Interested = 4,
    FollowUp = 5,
    Won = 6,
    Lost = 7,
    Closed = 8
}

public enum ConversationStatus : short
{
    Open = 1,
    Closed = 2
}

public enum ConversationMessageDirection : short
{
    Incoming = 1,
    Outgoing = 2
}

public enum ConversationMessageType : short
{
    Text = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Document = 5,
    Location = 6,
    Unsupported = 99
}

public enum ConversationMessageStatus : short
{
    Received = 1,
    Pending = 2,
    Sent = 3,
    Delivered = 4,
    Read = 5,
    Failed = 6
}

public enum ConversationFollowUpStatus : short
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3
}

public enum ConversationWebhookEventStatus : short
{
    Pending = 1,
    Processed = 2,
    Duplicate = 3,
    Failed = 4
}

public enum ConversationTemplateStatus : short
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Disabled = 4
}
