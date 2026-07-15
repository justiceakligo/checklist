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
