using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class ConversationsEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly LeadStatus[] TerminalLeadStatuses =
        [LeadStatus.Won, LeadStatus.Lost, LeadStatus.Closed];

    public static IEndpointRouteBuilder MapAtlasConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var conversations = app.MapGroup("/api/v1/conversations").WithTags("Conversations");
        conversations.MapGet("", ListConversations);
        conversations.MapPost("/manual", CreateManualConversation);
        conversations.MapGet("/{conversationId:guid}", GetConversation);
        conversations.MapPost("/{conversationId:guid}/assign", AssignConversation);
        conversations.MapPost("/{conversationId:guid}/unassign", UnassignConversation);
        conversations.MapPost("/{conversationId:guid}/messages", CreateConversationMessage);
        conversations.MapPost("/{conversationId:guid}/manual-messages", LogManualConversationMessage);
        conversations.MapPost("/{conversationId:guid}/notes", CreateInternalNote);
        conversations.MapPost("/{conversationId:guid}/follow-ups", CreateFollowUp);
        conversations.MapPatch("/{conversationId:guid}/lead-status", UpdateLeadStatus);

        var reports = app.MapGroup("/api/v1/conversation-reports").WithTags("Conversation Reports");
        reports.MapGet("/summary", GetConversationSummaryReport);

        var connections = app.MapGroup("/api/v1/whatsapp-connections").WithTags("WhatsApp Connections");
        connections.MapGet("/onboarding-options", GetWhatsAppOnboardingOptions);
        connections.MapGet("", ListWhatsAppConnections);
        connections.MapPost("", CreateWhatsAppConnection);
        connections.MapPost("/{connectionId:guid}/validate", ValidateWhatsAppConnection);

        var webhook = app.MapGroup("/api/webhooks/meta/whatsapp").WithTags("Meta Webhooks");
        webhook.MapGet("", VerifyMetaWebhook);
        webhook.MapPost("", ReceiveMetaWebhook);

        return app;
    }

    private static async Task<IResult> ListConversations(
        string? queue,
        Guid? assigneeId,
        LeadStatus? status,
        string? search,
        DateTimeOffset? updatedAfter,
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
        var query = ApplyConversationVisibility(dbContext.Conversations.AsNoTracking(), tenantContext)
            .Include(item => item.Connection)
            .Include(item => item.Contact)
            .Include(item => item.Lead)
            .Include(item => item.AssignedUser)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(item => item.Lead != null && item.Lead.Status == status.Value);
        }

        if (assigneeId.HasValue)
        {
            query = query.Where(item => item.AssignedUserId == assigneeId.Value);
        }

        if (updatedAfter.HasValue)
        {
            query = query.Where(item => item.UpdatedAt >= updatedAfter.Value);
        }

        query = ApplyQueueFilter(query, queue, tenantContext, clock.UtcNow);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var phone = NormalizePhone(term);
            query = query.Where(item =>
                item.Contact != null
                && ((item.Contact.DisplayName != null && item.Contact.DisplayName.Contains(term))
                    || item.Contact.NormalizedPhone.Contains(phone)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.LastMessageAt ?? item.UpdatedAt)
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .Select(item => new ConversationListItemResponse(
                item.Id,
                item.Status,
                item.Lead == null ? LeadStatus.New : item.Lead.Status,
                item.ConnectionId,
                item.Connection == null ? string.Empty : item.Connection.DisplayNumber,
                item.ContactId,
                item.Contact == null ? string.Empty : item.Contact.NormalizedPhone,
                item.Contact == null ? null : item.Contact.DisplayName,
                item.AssignedUserId,
                item.AssignedUser == null ? null : item.AssignedUser.FullName,
                item.AssignedTeamId,
                item.UnreadCount,
                item.LastMessageAt,
                item.LastInboundMessageAt,
                item.LastOutboundMessageAt,
                item.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .Select(message => message.Body)
                    .FirstOrDefault(),
                item.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            items,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            total
        });
    }

    private static async Task<IResult> GetConversation(
        Guid conversationId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var conversation = await LoadConversationDetailQuery(dbContext, tenantContext)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        return conversation is null
            ? EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound)
            : Results.Ok(ToConversationDetail(conversation));
    }

    private static async Task<IResult> CreateManualConversation(
        CreateManualConversationRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.create_manual");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        if (request.ConnectionId == Guid.Empty)
        {
            return EndpointHelpers.Problem("validation_failed", "A WhatsApp connection is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var normalizedPhone = NormalizePhone(request.CustomerPhone ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return EndpointHelpers.Problem("validation_failed", "Customer phone is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var requestedStatus = request.LeadStatus ?? LeadStatus.New;
        if (TerminalLeadStatuses.Contains(requestedStatus))
        {
            return EndpointHelpers.Problem("validation_failed", "Manual conversations must start with an open lead status.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.AssignedUserId.HasValue
            && !await IsActiveOrganizationMemberAsync(dbContext, request.AssignedUserId.Value, cancellationToken))
        {
            return EndpointHelpers.Problem("invalid_assignee", "Assignee must be an active member of this organization.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.AssignedTeamId.HasValue
            && !await IsActiveOrganizationTeamAsync(dbContext, request.AssignedTeamId.Value, cancellationToken))
        {
            return EndpointHelpers.Problem("invalid_team", "Assigned team must be an active team in this organization.", StatusCodes.Status422UnprocessableEntity);
        }

        var connection = await dbContext.WhatsAppConnections
            .FirstOrDefaultAsync(item => item.Id == request.ConnectionId, cancellationToken);
        if (connection is null)
        {
            return EndpointHelpers.Problem("not_found", "WhatsApp connection was not found.", StatusCodes.Status404NotFound);
        }

        if (connection.Status is WhatsAppConnectionStatus.Disabled or WhatsAppConnectionStatus.Revoked or WhatsAppConnectionStatus.Invalid)
        {
            return EndpointHelpers.Problem("invalid_connection", "This WhatsApp connection cannot accept manual conversations.", StatusCodes.Status422UnprocessableEntity);
        }

        var contact = await UpsertContactAsync(
            dbContext,
            connection,
            normalizedPhone,
            Truncate(request.CustomerName?.Trim(), 180),
            "manual_whatsapp",
            clock,
            cancellationToken);
        var lead = await UpsertLeadAsync(
            dbContext,
            access.OrganizationId,
            contact,
            "manual_whatsapp",
            Truncate(request.CampaignRef?.Trim(), 160),
            clock,
            cancellationToken);
        var conversation = await UpsertConversationAsync(dbContext, connection, contact, lead, clock, cancellationToken);

        var hasAssignment = request.AssignedUserId.HasValue || request.AssignedTeamId.HasValue;
        lead.Status = hasAssignment && requestedStatus == LeadStatus.New ? LeadStatus.Assigned : requestedStatus;
        conversation.AssignedUserId = request.AssignedUserId;
        conversation.AssignedTeamId = request.AssignedTeamId;
        conversation.Status = ConversationStatus.Open;
        conversation.ClosedAt = null;
        conversation.UpdatedAt = clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.InitialNote))
        {
            var note = new InternalNote
            {
                OrganizationId = access.OrganizationId,
                ConversationId = conversation.Id,
                AuthorUserId = tenantContext.UserId!.Value,
                Body = Truncate(request.InitialNote.Trim(), 2000) ?? string.Empty,
                CreatedAt = clock.UtcNow
            };
            dbContext.InternalNotes.Add(note);
        }

        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "conversation.manual_created",
            tenantContext,
            new
            {
                connection.Mode,
                ContactId = contact.Id,
                LeadId = lead.Id,
                request.AssignedUserId,
                request.AssignedTeamId
            });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.manual_created",
            new { ConversationId = conversation.Id, ContactId = contact.Id, LeadId = lead.Id, connection.Mode },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created(
            $"/api/v1/conversations/{conversation.Id}",
            new ManualConversationCreatedResponse(
                conversation.Id,
                connection.Id,
                contact.Id,
                lead.Id,
                contact.NormalizedPhone,
                contact.DisplayName,
                lead.Status,
                conversation.AssignedUserId,
                conversation.AssignedTeamId,
                BuildWaMeUrl(contact.NormalizedPhone),
                connection.Mode == WhatsAppConnectionMode.ManualOnly));
    }

    private static async Task<IResult> AssignConversation(
        Guid conversationId,
        AssignConversationRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.assign");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        if (request.UserId is null && request.TeamId is null)
        {
            return EndpointHelpers.Problem("validation_failed", "Assign a user, a team, or both.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.UserId.HasValue
            && !await IsActiveOrganizationMemberAsync(dbContext, request.UserId.Value, cancellationToken))
        {
            return EndpointHelpers.Problem("invalid_assignee", "Assignee must be an active member of this organization.", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.TeamId.HasValue
            && !await IsActiveOrganizationTeamAsync(dbContext, request.TeamId.Value, cancellationToken))
        {
            return EndpointHelpers.Problem("invalid_team", "Assigned team must be an active team in this organization.", StatusCodes.Status422UnprocessableEntity);
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var previousUserId = conversation.AssignedUserId;
        conversation.AssignedUserId = request.UserId;
        conversation.AssignedTeamId = request.TeamId;
        conversation.Status = ConversationStatus.Open;
        conversation.ClosedAt = null;
        if (conversation.Lead.Status == LeadStatus.New || conversation.Lead.Status == LeadStatus.Assigned)
        {
            conversation.Lead.Status = LeadStatus.Assigned;
        }

        dbContext.ConversationAssignments.Add(new ConversationAssignment
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversation.Id,
            FromUserId = previousUserId,
            ToUserId = request.UserId,
            AssignedTeamId = request.TeamId,
            AssignedByUserId = tenantContext.UserId!.Value,
            Reason = Truncate(request.Reason?.Trim(), 500),
            AssignedAt = clock.UtcNow
        });
        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "conversation.assigned",
            tenantContext,
            new { FromUserId = previousUserId, ToUserId = request.UserId, request.TeamId });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.assigned",
            new { ConversationId = conversation.Id, FromUserId = previousUserId, ToUserId = request.UserId, request.TeamId },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ConversationAssignedResponse(conversation.Id, conversation.AssignedUserId, conversation.AssignedTeamId, conversation.Lead.Status));
    }

    private static async Task<IResult> UnassignConversation(
        Guid conversationId,
        UnassignConversationRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.assign");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var previousUserId = conversation.AssignedUserId;
        conversation.AssignedUserId = null;
        conversation.AssignedTeamId = null;
        if (conversation.Lead.Status == LeadStatus.Assigned)
        {
            conversation.Lead.Status = LeadStatus.New;
        }

        dbContext.ConversationAssignments.Add(new ConversationAssignment
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversation.Id,
            FromUserId = previousUserId,
            ToUserId = null,
            AssignedByUserId = tenantContext.UserId!.Value,
            Reason = Truncate(request.Reason?.Trim(), 500),
            AssignedAt = clock.UtcNow
        });
        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "conversation.unassigned",
            tenantContext,
            new { FromUserId = previousUserId });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.unassigned",
            new { ConversationId = conversation.Id, FromUserId = previousUserId },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ConversationAssignedResponse(conversation.Id, null, null, conversation.Lead.Status));
    }

    private static async Task<IResult> CreateConversationMessage(
        Guid conversationId,
        CreateConversationMessageRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.reply");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return EndpointHelpers.Problem("validation_failed", "Message body is required for MVP text replies.", StatusCodes.Status422UnprocessableEntity);
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Connection)
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Connection is null || conversation.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        if (conversation.Connection.Mode == WhatsAppConnectionMode.ManualOnly)
        {
            return EndpointHelpers.Problem(
                "manual_reply_required",
                "This connection is manual-only. Open WhatsApp externally and log the sent reply with the manual message endpoint.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (!CanSendFreeformMessage(conversation, clock.UtcNow) && string.IsNullOrWhiteSpace(request.TemplateName))
        {
            return EndpointHelpers.Problem(
                "message_template_required",
                "The 24-hour WhatsApp customer-service window has expired. Send an approved template message.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var message = new ConversationMessage
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversation.Id,
            Direction = ConversationMessageDirection.Outgoing,
            Type = request.Type ?? ConversationMessageType.Text,
            Body = body,
            Status = ConversationMessageStatus.Pending,
            CreatedByUserId = tenantContext.UserId,
            MetadataJson = JsonSerializer.Serialize(new
            {
                request.ClientMessageId,
                TemplateName = request.TemplateName,
                AcceptedForAsyncSend = true
            }, JsonOptions),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        dbContext.ConversationMessages.Add(message);
        conversation.LastMessageAt = clock.UtcNow;
        conversation.LastOutboundMessageAt = clock.UtcNow;
        conversation.UnreadCount = 0;
        conversation.Connection.LastOutboundAt = clock.UtcNow;
        if (conversation.Lead.Status is LeadStatus.New or LeadStatus.Assigned)
        {
            conversation.Lead.Status = LeadStatus.Contacted;
        }

        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "message.outgoing_queued",
            tenantContext,
            new { MessageId = message.Id, message.Type, HasTemplate = !string.IsNullOrWhiteSpace(request.TemplateName) });
        dbContext.UsageEvents.Add(new UsageEvent
        {
            OrganizationId = access.OrganizationId,
            EventType = "conversation_message_queued",
            Quantity = 1,
            Unit = "message",
            IdempotencyKey = request.ClientMessageId is null
                ? $"conversation_message:{message.Id:N}"
                : $"conversation_message:{conversation.Id:N}:{request.ClientMessageId}",
            OccurredAt = clock.UtcNow,
            CreatedAt = clock.UtcNow
        });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.message_queued",
            new { ConversationId = conversation.Id, MessageId = message.Id, message.Type },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/api/v1/conversations/{conversation.Id}", ToMessageResponse(message));
    }

    private static async Task<IResult> LogManualConversationMessage(
        Guid conversationId,
        LogManualConversationMessageRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.reply");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return EndpointHelpers.Problem("validation_failed", "Message body is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Connection)
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Connection is null || conversation.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var sentAt = request.SentAt ?? clock.UtcNow;
        if (sentAt > clock.UtcNow.AddMinutes(5))
        {
            return EndpointHelpers.Problem("validation_failed", "Manual sent time cannot be in the future.", StatusCodes.Status422UnprocessableEntity);
        }

        var message = new ConversationMessage
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversation.Id,
            Direction = ConversationMessageDirection.Outgoing,
            Type = ConversationMessageType.Text,
            Body = body,
            Status = ConversationMessageStatus.Sent,
            CreatedByUserId = tenantContext.UserId,
            SentAt = sentAt,
            MetadataJson = JsonSerializer.Serialize(new
            {
                request.ClientMessageId,
                ManualExternalSend = true,
                Provider = "manual_whatsapp"
            }, JsonOptions),
            CreatedAt = sentAt,
            UpdatedAt = clock.UtcNow
        };

        dbContext.ConversationMessages.Add(message);
        conversation.LastMessageAt = sentAt;
        conversation.LastOutboundMessageAt = sentAt;
        conversation.UnreadCount = 0;
        conversation.Connection.LastOutboundAt = sentAt;
        if (conversation.Lead.Status is LeadStatus.New or LeadStatus.Assigned)
        {
            conversation.Lead.Status = LeadStatus.Contacted;
        }

        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "message.manual_outgoing_logged",
            tenantContext,
            new { MessageId = message.Id, conversation.Connection.Mode });
        dbContext.UsageEvents.Add(new UsageEvent
        {
            OrganizationId = access.OrganizationId,
            EventType = "conversation_manual_message_logged",
            Quantity = 1,
            Unit = "message",
            IdempotencyKey = request.ClientMessageId is null
                ? $"conversation_manual_message:{message.Id:N}"
                : $"conversation_manual_message:{conversation.Id:N}:{request.ClientMessageId}",
            OccurredAt = sentAt,
            CreatedAt = clock.UtcNow
        });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.manual_message_logged",
            new { ConversationId = conversation.Id, MessageId = message.Id },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/conversations/{conversation.Id}", ToMessageResponse(message));
    }

    private static async Task<IResult> CreateInternalNote(
        Guid conversationId,
        CreateInternalNoteRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.add_note");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        var body = request.Body?.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return EndpointHelpers.Problem("validation_failed", "Note body is required.", StatusCodes.Status422UnprocessableEntity);
        }

        if (!await dbContext.Conversations.AnyAsync(item => item.Id == conversationId, cancellationToken))
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var note = new InternalNote
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversationId,
            AuthorUserId = tenantContext.UserId!.Value,
            Body = body,
            CreatedAt = clock.UtcNow
        };
        dbContext.InternalNotes.Add(note);
        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversationId,
            "note.created",
            tenantContext,
            new { NoteId = note.Id });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.note_created",
            new { ConversationId = conversationId, NoteId = note.Id },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/conversations/{conversationId}", ToNoteResponse(note, null));
    }

    private static async Task<IResult> CreateFollowUp(
        Guid conversationId,
        CreateFollowUpRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.manage_followups");
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (RequireUser(tenantContext) is { } userProblem)
        {
            return userProblem;
        }

        if (request.DueAt <= clock.UtcNow)
        {
            return EndpointHelpers.Problem("validation_failed", "Follow-up due date must be in the future.", StatusCodes.Status422UnprocessableEntity);
        }

        var ownerUserId = request.OwnerUserId ?? tenantContext.UserId!.Value;
        if (!await IsActiveOrganizationMemberAsync(dbContext, ownerUserId, cancellationToken))
        {
            return EndpointHelpers.Problem("invalid_owner", "Follow-up owner must be an active member of this organization.", StatusCodes.Status422UnprocessableEntity);
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var followUp = new FollowUp
        {
            OrganizationId = access.OrganizationId,
            ConversationId = conversation.Id,
            OwnerUserId = ownerUserId,
            DueAt = request.DueAt,
            Note = Truncate(request.Note?.Trim(), 2000),
            Status = ConversationFollowUpStatus.Pending,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        dbContext.FollowUps.Add(followUp);
        if (!TerminalLeadStatuses.Contains(conversation.Lead.Status))
        {
            conversation.Lead.Status = LeadStatus.FollowUp;
        }

        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "follow_up.created",
            tenantContext,
            new { FollowUpId = followUp.Id, followUp.OwnerUserId, followUp.DueAt });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.follow_up_created",
            new { ConversationId = conversation.Id, FollowUpId = followUp.Id, followUp.OwnerUserId, followUp.DueAt },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/conversations/{conversation.Id}", ToFollowUpResponse(followUp, null));
    }

    private static async Task<IResult> UpdateLeadStatus(
        Guid conversationId,
        UpdateLeadStatusRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations.update_status");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var conversation = await dbContext.Conversations
            .Include(item => item.Lead)
            .FirstOrDefaultAsync(item => item.Id == conversationId, cancellationToken);
        if (conversation?.Lead is null)
        {
            return EndpointHelpers.Problem("not_found", "Conversation was not found.", StatusCodes.Status404NotFound);
        }

        var previousStatus = conversation.Lead.Status;
        conversation.Lead.Status = request.Status;
        conversation.Lead.LostReason = request.Status == LeadStatus.Lost ? Truncate(request.LostReason?.Trim(), 500) : null;
        conversation.Lead.WonAt = request.Status == LeadStatus.Won ? clock.UtcNow : conversation.Lead.WonAt;

        if (TerminalLeadStatuses.Contains(request.Status))
        {
            conversation.Status = ConversationStatus.Closed;
            conversation.ClosedAt ??= clock.UtcNow;
        }
        else
        {
            conversation.Status = ConversationStatus.Open;
            conversation.ClosedAt = null;
        }

        AddConversationActivity(
            dbContext,
            access.OrganizationId,
            conversation.Id,
            "lead.status_updated",
            tenantContext,
            new { From = previousStatus, To = request.Status });
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "conversation.lead_status_updated",
            new { ConversationId = conversation.Id, From = previousStatus, To = request.Status },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new LeadStatusUpdatedResponse(conversation.Id, previousStatus, conversation.Lead.Status, conversation.Status));
    }

    private static async Task<IResult> GetConversationSummaryReport(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "conversations_reports.view");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var period = ResolvePeriod(from, to, clock.UtcNow);
        var leads = await dbContext.Leads.AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var conversations = await dbContext.Conversations.AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var messages = await dbContext.ConversationMessages.AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var dueFollowUps = await dbContext.FollowUps.AsNoTracking()
            .CountAsync(item => item.Status == ConversationFollowUpStatus.Pending && item.DueAt <= clock.UtcNow, cancellationToken);

        return Results.Ok(new ConversationSummaryReportResponse(
            period.From,
            period.To,
            leads.Count,
            leads.Count(item => item.Status == LeadStatus.Won),
            leads.Count(item => item.Status == LeadStatus.Lost),
            conversations.Count,
            conversations.Count(item => item.AssignedUserId == null && item.AssignedTeamId == null),
            dueFollowUps,
            messages.Count(item => item.Direction == ConversationMessageDirection.Incoming),
            messages.Count(item => item.Direction == ConversationMessageDirection.Outgoing),
            Enum.GetValues<LeadStatus>().ToDictionary(
                item => item.ToString(),
                item => leads.Count(lead => lead.Status == item))));
    }

    private static async Task<IResult> GetWhatsAppOnboardingOptions(
        HttpRequest request,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "whatsapp_connections.manage");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var baseUrl = $"{request.Scheme}://{request.Host}";
        return Results.Ok(new WhatsAppOnboardingOptionsResponse(
            $"{baseUrl}/api/webhooks/meta/whatsapp",
            [
                new WhatsAppOnboardingModeResponse(
                    WhatsAppConnectionMode.CloudApi,
                    "Connect a Meta Cloud API number",
                    "Use this for a number that already has WhatsApp Business Platform assets.",
                    RequiresEmbeddedSignup: false,
                    SupportsExistingBusinessApp: false,
                    SupportsPersonalWhatsapp: false,
                    CanReceiveWebhooks: true,
                    CanSendViaApi: true,
                    RequiresExternalReply: false,
                    RequiredCreateFields: ["mode", "phoneNumberId", "wabaId", "displayNumber"],
                    UnsupportedReason: null),
                new WhatsAppOnboardingModeResponse(
                    WhatsAppConnectionMode.BusinessAppCoexistence,
                    "Connect an existing WhatsApp Business App number",
                    "Use Meta Embedded Signup/coexistence so the customer can keep the Business App while Reqara receives API/webhook traffic.",
                    RequiresEmbeddedSignup: true,
                    SupportsExistingBusinessApp: true,
                    SupportsPersonalWhatsapp: false,
                    CanReceiveWebhooks: true,
                    CanSendViaApi: true,
                    RequiresExternalReply: false,
                    RequiredCreateFields: ["mode", "phoneNumberId", "wabaId", "displayNumber"],
                    UnsupportedReason: "Personal WhatsApp Messenger numbers are not eligible for this API-backed mode."),
                new WhatsAppOnboardingModeResponse(
                    WhatsAppConnectionMode.ManualOnly,
                    "Track any WhatsApp number manually",
                    "Use this when the person only wants to keep using WhatsApp or the WhatsApp Business App without connecting Meta API assets.",
                    RequiresEmbeddedSignup: false,
                    SupportsExistingBusinessApp: true,
                    SupportsPersonalWhatsapp: true,
                    CanReceiveWebhooks: false,
                    CanSendViaApi: false,
                    RequiresExternalReply: true,
                    RequiredCreateFields: ["mode", "displayNumber"],
                    UnsupportedReason: "No live message sync, delivery status, or provider sending is available in manual-only mode.")
            ]));
    }

    private static async Task<IResult> ListWhatsAppConnections(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "whatsapp_connections.manage");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var connections = await dbContext.WhatsAppConnections.AsNoTracking()
            .OrderBy(item => item.DisplayNumber)
            .ToListAsync(cancellationToken);
        var items = connections.Select(ToConnectionResponse).ToList();
        return Results.Ok(new { items, total = items.Count });
    }

    private static async Task<IResult> CreateWhatsAppConnection(
        CreateWhatsAppConnectionRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "whatsapp_connections.manage");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var mode = request.Mode ?? WhatsAppConnectionMode.CloudApi;
        var displayNumber = NormalizePhone(request.DisplayNumber ?? string.Empty);
        if (string.IsNullOrWhiteSpace(displayNumber))
        {
            return EndpointHelpers.Problem("validation_failed", "Display number is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var phoneNumberId = string.IsNullOrWhiteSpace(request.PhoneNumberId) ? null : request.PhoneNumberId.Trim();
        var wabaId = string.IsNullOrWhiteSpace(request.WabaId) ? null : request.WabaId.Trim();
        if ((mode == WhatsAppConnectionMode.CloudApi || mode == WhatsAppConnectionMode.BusinessAppCoexistence)
            && (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(wabaId)))
        {
            return EndpointHelpers.Problem("validation_failed", "Phone number ID, WABA ID and display number are required for API-backed WhatsApp connections.", StatusCodes.Status422UnprocessableEntity);
        }

        if (!string.IsNullOrWhiteSpace(phoneNumberId)
            && await dbContext.WhatsAppConnections.AnyAsync(item => item.PhoneNumberId == phoneNumberId, cancellationToken))
        {
            return EndpointHelpers.Problem("duplicate_connection", "This WhatsApp phone number is already connected.", StatusCodes.Status409Conflict);
        }

        if (mode == WhatsAppConnectionMode.ManualOnly
            && await dbContext.WhatsAppConnections.AnyAsync(
                item => item.Mode == WhatsAppConnectionMode.ManualOnly && item.DisplayNumber == displayNumber,
                cancellationToken))
        {
            return EndpointHelpers.Problem("duplicate_connection", "This manual WhatsApp number is already connected.", StatusCodes.Status409Conflict);
        }

        var connection = new WhatsAppConnection
        {
            OrganizationId = access.OrganizationId,
            Mode = mode,
            PhoneNumberId = phoneNumberId,
            WabaId = wabaId,
            DisplayNumber = displayNumber,
            SecretReference = Truncate(request.SecretReference?.Trim(), 300),
            WebhookVerifyTokenHash = string.IsNullOrWhiteSpace(request.WebhookVerifyToken)
                ? null
                : secretHasher.HashSecret(request.WebhookVerifyToken.Trim()),
            BusinessPortfolioId = Truncate(request.BusinessPortfolioId?.Trim(), 120),
            EmbeddedSignupSessionId = Truncate(request.EmbeddedSignupSessionId?.Trim(), 160),
            ExternalAccountId = Truncate(request.ExternalAccountId?.Trim(), 160),
            HistorySyncRequested = request.HistorySyncRequested.GetValueOrDefault(),
            Status = mode == WhatsAppConnectionMode.ManualOnly
                ? WhatsAppConnectionStatus.Active
                : WhatsAppConnectionStatus.PendingVerification,
            ConnectedAt = mode == WhatsAppConnectionMode.ManualOnly ? clock.UtcNow : null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                CreatedFrom = request.OnboardingSource ?? "api",
                mode,
                Notes = Truncate(request.Notes?.Trim(), 1000)
            }, JsonOptions),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        dbContext.WhatsAppConnections.Add(connection);
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "whatsapp_connection.created",
            new { connection.Id, connection.Mode, connection.PhoneNumberId, connection.WabaId, connection.DisplayNumber },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/whatsapp-connections/{connection.Id}", ToConnectionResponse(connection));
    }

    private static async Task<IResult> ValidateWhatsAppConnection(
        Guid connectionId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireConversationsAccessAsync(tenantContext, entitlements, clock, cancellationToken, "whatsapp_connections.manage");
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var connection = await dbContext.WhatsAppConnections.FirstOrDefaultAsync(item => item.Id == connectionId, cancellationToken);
        if (connection is null)
        {
            return EndpointHelpers.Problem("not_found", "WhatsApp connection was not found.", StatusCodes.Status404NotFound);
        }

        if (connection.Mode == WhatsAppConnectionMode.ManualOnly)
        {
            return EndpointHelpers.Problem("manual_validation_not_supported", "Manual-only WhatsApp connections do not use Meta webhook validation.", StatusCodes.Status422UnprocessableEntity);
        }

        connection.Status = WhatsAppConnectionStatus.Active;
        connection.VerifiedAt ??= clock.UtcNow;
        connection.ConnectedAt ??= clock.UtcNow;
        connection.LastValidatedAt = clock.UtcNow;
        SecurityEndpoints.AddAudit(
            dbContext,
            access.OrganizationId,
            null,
            tenantContext,
            "whatsapp_connection.validated",
            new { connection.Id, connection.PhoneNumberId },
            httpContext);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToConnectionResponse(connection));
    }

    private static async Task<IResult> VerifyMetaWebhook(
        HttpRequest request,
        AtlasDbContext dbContext,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var mode = request.Query["hub.mode"].FirstOrDefault();
        var token = request.Query["hub.verify_token"].FirstOrDefault();
        var challenge = request.Query["hub.challenge"].FirstOrDefault();
        if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(challenge))
        {
            return EndpointHelpers.Problem("validation_failed", "Meta webhook verification query is invalid.", StatusCodes.Status400BadRequest);
        }

        var candidates = await dbContext.WhatsAppConnections.IgnoreQueryFilters()
            .Where(item => item.WebhookVerifyTokenHash != null
                && item.Status != WhatsAppConnectionStatus.Disabled
                && item.Status != WhatsAppConnectionStatus.Revoked)
            .ToListAsync(cancellationToken);
        var connection = candidates.FirstOrDefault(item =>
            item.WebhookVerifyTokenHash is not null && secretHasher.VerifySecret(token, item.WebhookVerifyTokenHash));
        if (connection is null)
        {
            return EndpointHelpers.Problem("forbidden", "Webhook verify token was not accepted.", StatusCodes.Status403Forbidden);
        }

        connection.Status = WhatsAppConnectionStatus.Active;
        connection.VerifiedAt ??= clock.UtcNow;
        connection.ConnectedAt ??= clock.UtcNow;
        connection.LastValidatedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Text(challenge, "text/plain", Encoding.UTF8);
    }

    private static async Task<IResult> ReceiveMetaWebhook(
        HttpRequest request,
        AtlasDbContext dbContext,
        IAdminSettingService settings,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return EndpointHelpers.Problem("validation_failed", "Webhook payload is required.", StatusCodes.Status400BadRequest);
        }

        var appSecret = EndpointHelpers.ReadStringSetting(
            (await settings.GetAsync(null, "whatsapp", "webhookAppSecret", cancellationToken))?.ValueJson);
        if (!string.IsNullOrWhiteSpace(appSecret)
            && !ValidateMetaSignature(body, request.Headers["X-Hub-Signature-256"].FirstOrDefault(), appSecret))
        {
            return EndpointHelpers.Problem("invalid_signature", "Meta webhook signature is invalid.", StatusCodes.Status401Unauthorized);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return EndpointHelpers.Problem("invalid_json", "Webhook payload is not valid JSON.", StatusCodes.Status400BadRequest);
        }

        using (document)
        {
            var root = document.RootElement.Clone();
            var phoneNumberId = ExtractMetaPhoneNumberId(root);
            if (string.IsNullOrWhiteSpace(phoneNumberId))
            {
                return Results.Ok(new { received = true, ignored = true, reason = "phone_number_id_missing" });
            }

            var connection = await dbContext.WhatsAppConnections.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    item => item.PhoneNumberId == phoneNumberId
                        && item.Mode != WhatsAppConnectionMode.ManualOnly
                        && item.Status != WhatsAppConnectionStatus.Disabled
                        && item.Status != WhatsAppConnectionStatus.Revoked,
                    cancellationToken);
            if (connection is null)
            {
                return Results.Ok(new { received = true, ignored = true, reason = "connection_not_found" });
            }

            var providerEventId = ExtractProviderEventId(root, body);
            var existing = await dbContext.ConversationWebhookEvents.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.ProviderEventId == providerEventId, cancellationToken);
            if (existing is not null)
            {
                existing.ProcessingStatus = ConversationWebhookEventStatus.Duplicate;
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { received = true, duplicate = true, eventId = existing.Id });
            }

            var webhookEvent = new ConversationWebhookEvent
            {
                OrganizationId = connection.OrganizationId,
                ConnectionId = connection.Id,
                ProviderEventId = providerEventId,
                EventType = ExtractMetaEventType(root),
                PayloadJson = body,
                ProcessingStatus = ConversationWebhookEventStatus.Pending,
                ReceivedAt = clock.UtcNow
            };
            dbContext.ConversationWebhookEvents.Add(webhookEvent);

            try
            {
                await ProcessMetaWebhookPayloadAsync(dbContext, connection, root, clock, cancellationToken);
                webhookEvent.ProcessingStatus = ConversationWebhookEventStatus.Processed;
                webhookEvent.ProcessedAt = clock.UtcNow;
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException)
            {
                webhookEvent.ProcessingStatus = ConversationWebhookEventStatus.Failed;
                webhookEvent.FailureCode = exception.GetType().Name;
                webhookEvent.FailureMessage = Truncate(exception.Message, 1000);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { received = true, eventId = webhookEvent.Id, status = webhookEvent.ProcessingStatus });
        }
    }

    private static async Task ProcessMetaWebhookPayloadAsync(
        AtlasDbContext dbContext,
        WhatsAppConnection connection,
        JsonElement root,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        foreach (var value in ExtractMetaValueObjects(root))
        {
            await ProcessIncomingMessagesAsync(dbContext, connection, value, clock, cancellationToken);
            await ProcessMessageStatusesAsync(dbContext, connection.OrganizationId, value, clock, cancellationToken);
        }
    }

    private static async Task ProcessIncomingMessagesAsync(
        AtlasDbContext dbContext,
        WhatsAppConnection connection,
        JsonElement value,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in messages.EnumerateArray())
        {
            var providerMessageId = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(providerMessageId)
                || await dbContext.ConversationMessages.IgnoreQueryFilters()
                    .AnyAsync(message => message.OrganizationId == connection.OrganizationId
                        && message.ProviderMessageId == providerMessageId, cancellationToken))
            {
                continue;
            }

            var from = NormalizePhone(ReadString(item, "from") ?? string.Empty);
            if (string.IsNullOrWhiteSpace(from))
            {
                continue;
            }

            var messageTime = ReadUnixTimestamp(item, "timestamp") ?? clock.UtcNow;
            var contactName = ExtractContactName(value, from);
            var contact = await UpsertContactAsync(dbContext, connection, from, contactName, "whatsapp", clock, cancellationToken);
            var lead = await UpsertLeadAsync(dbContext, connection.OrganizationId, contact, "whatsapp", null, clock, cancellationToken);
            var conversation = await UpsertConversationAsync(dbContext, connection, contact, lead, clock, cancellationToken);

            var type = MapMessageType(ReadString(item, "type"));
            var body = ExtractMessageBody(item, type);
            dbContext.ConversationMessages.Add(new ConversationMessage
            {
                OrganizationId = connection.OrganizationId,
                ConversationId = conversation.Id,
                ProviderMessageId = providerMessageId,
                Direction = ConversationMessageDirection.Incoming,
                Type = type,
                Body = body,
                Status = ConversationMessageStatus.Received,
                SentAt = messageTime,
                MetadataJson = JsonSerializer.Serialize(new { Provider = "meta_whatsapp" }, JsonOptions),
                CreatedAt = messageTime,
                UpdatedAt = clock.UtcNow
            });

            conversation.LastMessageAt = messageTime;
            conversation.LastInboundMessageAt = messageTime;
            conversation.UnreadCount += 1;
            connection.LastInboundAt = messageTime;
            AddSystemActivity(
                dbContext,
                connection.OrganizationId,
                conversation.Id,
                "message.incoming_received",
                new { ProviderMessageId = providerMessageId, Type = type });
        }
    }

    private static async Task ProcessMessageStatusesAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        JsonElement value,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!value.TryGetProperty("statuses", out var statuses) || statuses.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in statuses.EnumerateArray())
        {
            var providerMessageId = ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(providerMessageId))
            {
                continue;
            }

            var message = await dbContext.ConversationMessages.IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.OrganizationId == organizationId
                    && candidate.ProviderMessageId == providerMessageId, cancellationToken);
            if (message is null)
            {
                continue;
            }

            var status = ReadString(item, "status");
            var timestamp = ReadUnixTimestamp(item, "timestamp") ?? clock.UtcNow;
            message.Status = status?.Trim().ToLowerInvariant() switch
            {
                "sent" => ConversationMessageStatus.Sent,
                "delivered" => ConversationMessageStatus.Delivered,
                "read" => ConversationMessageStatus.Read,
                "failed" => ConversationMessageStatus.Failed,
                _ => message.Status
            };
            if (message.Status == ConversationMessageStatus.Sent)
            {
                message.SentAt ??= timestamp;
            }
            else if (message.Status == ConversationMessageStatus.Delivered)
            {
                message.DeliveredAt ??= timestamp;
            }
            else if (message.Status == ConversationMessageStatus.Read)
            {
                message.ReadAt ??= timestamp;
            }
            else if (message.Status == ConversationMessageStatus.Failed)
            {
                message.FailedAt ??= timestamp;
                message.FailureCode = ExtractStatusFailureCode(item);
            }

            AddSystemActivity(
                dbContext,
                organizationId,
                message.ConversationId,
                "message.status_updated",
                new { MessageId = message.Id, ProviderMessageId = providerMessageId, message.Status });
        }
    }

    private static async Task<Contact> UpsertContactAsync(
        AtlasDbContext dbContext,
        WhatsAppConnection connection,
        string normalizedPhone,
        string? displayName,
        string source,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OrganizationId == connection.OrganizationId
                && item.NormalizedPhone == normalizedPhone, cancellationToken);
        if (contact is not null)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                contact.DisplayName = displayName;
            }
            contact.ConnectionId ??= connection.Id;
            contact.UpdatedAt = clock.UtcNow;
            return contact;
        }

        contact = new Contact
        {
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            NormalizedPhone = normalizedPhone,
            DisplayName = displayName,
            ProfileJson = JsonSerializer.Serialize(new { Source = source }, JsonOptions),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        dbContext.Contacts.Add(contact);
        return contact;
    }

    private static async Task<Lead> UpsertLeadAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        Contact contact,
        string source,
        string? campaignRef,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var lead = await dbContext.Leads.IgnoreQueryFilters()
            .Where(item => item.OrganizationId == organizationId && item.ContactId == contact.Id)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(item => item.Status != LeadStatus.Closed
                && item.Status != LeadStatus.Lost
                && item.Status != LeadStatus.Won, cancellationToken);
        if (lead is not null)
        {
            if (!string.IsNullOrWhiteSpace(campaignRef))
            {
                lead.CampaignRef = campaignRef;
            }
            lead.UpdatedAt = clock.UtcNow;
            return lead;
        }

        lead = new Lead
        {
            OrganizationId = organizationId,
            ContactId = contact.Id,
            Status = LeadStatus.New,
            Source = source,
            CampaignRef = campaignRef,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        dbContext.Leads.Add(lead);
        return lead;
    }

    private static async Task<Conversation> UpsertConversationAsync(
        AtlasDbContext dbContext,
        WhatsAppConnection connection,
        Contact contact,
        Lead lead,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id
                && item.ContactId == contact.Id
                && item.Status == ConversationStatus.Open, cancellationToken);
        if (conversation is not null)
        {
            conversation.UpdatedAt = clock.UtcNow;
            return conversation;
        }

        conversation = new Conversation
        {
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            ContactId = contact.Id,
            LeadId = lead.Id,
            Status = ConversationStatus.Open,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        dbContext.Conversations.Add(conversation);
        AddSystemActivity(
            dbContext,
            connection.OrganizationId,
            conversation.Id,
            "conversation.created",
            new { ContactId = contact.Id, LeadId = lead.Id, ConnectionId = connection.Id });
        return conversation;
    }

    private static IQueryable<Conversation> LoadConversationDetailQuery(AtlasDbContext dbContext, ITenantContext tenantContext)
    {
        return ApplyConversationVisibility(dbContext.Conversations, tenantContext)
            .Include(item => item.Connection)
            .Include(item => item.Contact)
            .Include(item => item.Lead)
            .Include(item => item.AssignedUser)
            .Include(item => item.Messages)
            .ThenInclude(item => item.CreatedByUser)
            .Include(item => item.Assignments)
            .ThenInclude(item => item.ToUser)
            .Include(item => item.InternalNotes)
            .ThenInclude(item => item.AuthorUser)
            .Include(item => item.FollowUps)
            .ThenInclude(item => item.OwnerUser)
            .Include(item => item.Activities)
            .Include(item => item.Links);
    }

    private static IQueryable<Conversation> ApplyConversationVisibility(IQueryable<Conversation> query, ITenantContext tenantContext)
    {
        if (CanViewAllConversations(tenantContext))
        {
            return query;
        }

        return tenantContext.UserId.HasValue && EndpointHelpers.HasScope(tenantContext, "conversations.view_assigned")
            ? query.Where(item => item.AssignedUserId == tenantContext.UserId.Value)
            : query.Where(_ => false);
    }

    private static IQueryable<Conversation> ApplyQueueFilter(
        IQueryable<Conversation> query,
        string? queue,
        ITenantContext tenantContext,
        DateTimeOffset now)
    {
        return queue?.Trim().ToLowerInvariant() switch
        {
            "mine" when tenantContext.UserId.HasValue => query.Where(item => item.AssignedUserId == tenantContext.UserId.Value),
            "unassigned" => query.Where(item => item.AssignedUserId == null && item.AssignedTeamId == null),
            "new" => query.Where(item => item.AssignedUserId == null && item.AssignedTeamId == null && item.Lead != null && item.Lead.Status == LeadStatus.New),
            "follow-up" => query.Where(item => item.FollowUps.Any(followUp => followUp.Status == ConversationFollowUpStatus.Pending)),
            "closed" => query.Where(item => item.Status == ConversationStatus.Closed),
            _ => query.Where(item => item.Status == ConversationStatus.Open)
        };
    }

    private static async Task<ConversationAccess> RequireConversationsAccessAsync(
        ITenantContext tenantContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken,
        params string[] acceptedScopes)
    {
        if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
        {
            return new ConversationAccess(Guid.Empty, problem);
        }

        if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
        {
            return new ConversationAccess(Guid.Empty, sandboxProblem);
        }

        var entitlement = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "conversations.enabled", cancellationToken);
        if (!entitlement.Allowed)
        {
            return new ConversationAccess(Guid.Empty, EndpointHelpers.EntitlementProblem(entitlement));
        }

        if (acceptedScopes.Length > 0 && !acceptedScopes.Any(scope => HasConversationScope(tenantContext, scope)))
        {
            return new ConversationAccess(Guid.Empty, EndpointHelpers.Problem("forbidden", "Conversation permission is required.", StatusCodes.Status403Forbidden));
        }

        return new ConversationAccess(organizationId, null);
    }

    private static bool HasConversationScope(ITenantContext tenantContext, string scope)
    {
        return tenantContext.Scopes.Contains(scope)
            || tenantContext.Scopes.Contains("*")
            || tenantContext.Scopes.Contains("dashboard:*")
            || tenantContext.Scopes.Contains("admin:*")
            || tenantContext.Scopes.Contains("conversations:*")
            || (scope.StartsWith("whatsapp_connections.", StringComparison.Ordinal)
                && tenantContext.Scopes.Contains("whatsapp_connections:*"));
    }

    private static bool CanViewAllConversations(ITenantContext tenantContext)
    {
        return HasConversationScope(tenantContext, "conversations.view_all");
    }

    private static IResult? RequireUser(ITenantContext tenantContext)
    {
        return tenantContext.UserId.HasValue
            ? null
            : EndpointHelpers.Problem("user_required", "A dashboard user is required for this conversation action.", StatusCodes.Status403Forbidden);
    }

    private static async Task<bool> IsActiveOrganizationMemberAsync(
        AtlasDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OrganizationUsers.AnyAsync(
            item => item.UserId == userId && item.Status == MembershipStatus.Active,
            cancellationToken);
    }

    private static async Task<bool> IsActiveOrganizationTeamAsync(
        AtlasDbContext dbContext,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        return await dbContext.OrganizationTeams.AnyAsync(
            item => item.Id == teamId && item.Status == OrganizationTeamStatus.Active,
            cancellationToken);
    }

    private static bool CanSendFreeformMessage(Conversation conversation, DateTimeOffset now)
    {
        return conversation.LastInboundMessageAt.HasValue
            && conversation.LastInboundMessageAt.Value >= now.AddHours(-24);
    }

    private static bool ValidateMetaSignature(string body, string? signatureHeader, string appSecret)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrWhiteSpace(signatureHeader)
            || !signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(appSecret),
            Encoding.UTF8.GetBytes(body));
        var actualHex = signatureHeader[prefix.Length..].Trim();
        if (actualHex.Length != expected.Length * 2)
        {
            return false;
        }

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(actualHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return actual.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static IEnumerable<JsonElement> ExtractMetaValueObjects(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                {
                    yield return value;
                }
            }
        }
    }

    private static string? ExtractMetaPhoneNumberId(JsonElement root)
    {
        foreach (var value in ExtractMetaValueObjects(root))
        {
            if (value.TryGetProperty("metadata", out var metadata)
                && metadata.TryGetProperty("phone_number_id", out var phoneNumberId)
                && phoneNumberId.ValueKind == JsonValueKind.String)
            {
                return phoneNumberId.GetString();
            }
        }

        return null;
    }

    private static string ExtractMetaEventType(JsonElement root)
    {
        foreach (var value in ExtractMetaValueObjects(root))
        {
            if (value.TryGetProperty("messages", out _))
            {
                return "messages";
            }

            if (value.TryGetProperty("statuses", out _))
            {
                return "statuses";
            }
        }

        return "unknown";
    }

    private static string ExtractProviderEventId(JsonElement root, string body)
    {
        foreach (var value in ExtractMetaValueObjects(root))
        {
            if (value.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                var first = messages.EnumerateArray().FirstOrDefault();
                var id = ReadString(first, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return $"message:{id}";
                }
            }

            if (value.TryGetProperty("statuses", out var statuses) && statuses.ValueKind == JsonValueKind.Array)
            {
                var first = statuses.EnumerateArray().FirstOrDefault();
                var id = ReadString(first, "id");
                var status = ReadString(first, "status") ?? "unknown";
                var timestamp = ReadString(first, "timestamp") ?? "0";
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return $"status:{id}:{status}:{timestamp}";
                }
            }
        }

        return "payload:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static string? ExtractContactName(JsonElement value, string normalizedPhone)
    {
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var contact in contacts.EnumerateArray())
        {
            var phone = NormalizePhone(ReadString(contact, "wa_id") ?? string.Empty);
            if (phone != normalizedPhone)
            {
                continue;
            }

            if (contact.TryGetProperty("profile", out var profile))
            {
                return ReadString(profile, "name");
            }
        }

        return null;
    }

    private static string? ExtractMessageBody(JsonElement message, ConversationMessageType type)
    {
        if (type == ConversationMessageType.Text
            && message.TryGetProperty("text", out var text))
        {
            return ReadString(text, "body");
        }

        if (message.TryGetProperty(type.ToString().ToLowerInvariant(), out var media))
        {
            return ReadString(media, "caption") ?? $"[{type}]";
        }

        return type == ConversationMessageType.Unsupported ? "[unsupported message]" : $"[{type}]";
    }

    private static string? ExtractStatusFailureCode(JsonElement status)
    {
        if (!status.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var first = errors.EnumerateArray().FirstOrDefault();
        return ReadString(first, "code") ?? ReadString(first, "title");
    }

    private static ConversationMessageType MapMessageType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "text" => ConversationMessageType.Text,
            "image" => ConversationMessageType.Image,
            "video" => ConversationMessageType.Video,
            "audio" => ConversationMessageType.Audio,
            "document" => ConversationMessageType.Document,
            "location" => ConversationMessageType.Location,
            _ => ConversationMessageType.Unsupported
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static DateTimeOffset? ReadUnixTimestamp(JsonElement element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return long.TryParse(raw, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolvePeriod(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset now)
    {
        var resolvedTo = to ?? now;
        var resolvedFrom = from ?? resolvedTo.AddDays(-30);
        return resolvedFrom <= resolvedTo
            ? (resolvedFrom, resolvedTo)
            : (resolvedTo, resolvedFrom);
    }

    private static string NormalizePhone(string value)
    {
        var trimmed = value.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits)
            ? string.Empty
            : hasPlus ? $"+{digits}" : digits;
    }

    private static string? BuildWaMeUrl(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : $"https://wa.me/{digits}";
    }

    private static void AddConversationActivity(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid conversationId,
        string type,
        ITenantContext tenantContext,
        object metadata)
    {
        var actorType = tenantContext.ActorType switch
        {
            "user" => ActorType.User,
            "api" => ActorType.Api,
            "recipient" => ActorType.Recipient,
            _ => ActorType.System
        };

        dbContext.ConversationActivities.Add(new ConversationActivity
        {
            OrganizationId = organizationId,
            ConversationId = conversationId,
            Type = type,
            ActorType = actorType,
            ActorId = tenantContext.ActorId,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        });
    }

    private static void AddSystemActivity(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid conversationId,
        string type,
        object metadata)
    {
        dbContext.ConversationActivities.Add(new ConversationActivity
        {
            OrganizationId = organizationId,
            ConversationId = conversationId,
            Type = type,
            ActorType = ActorType.System,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
        });
    }

    private static ConversationDetailResponse ToConversationDetail(Conversation conversation)
    {
        return new ConversationDetailResponse(
            conversation.Id,
            conversation.Status,
            ToConnectionResponse(conversation.Connection),
            ToContactResponse(conversation.Contact),
            ToLeadResponse(conversation.Lead),
            conversation.AssignedUserId,
            conversation.AssignedUser?.FullName,
            conversation.AssignedTeamId,
            conversation.UnreadCount,
            conversation.LastMessageAt,
            conversation.LastInboundMessageAt,
            conversation.LastOutboundMessageAt,
            conversation.Messages.OrderBy(item => item.CreatedAt).Select(ToMessageResponse).ToList(),
            conversation.InternalNotes.OrderByDescending(item => item.CreatedAt).Select(item => ToNoteResponse(item, item.AuthorUser)).ToList(),
            conversation.FollowUps.OrderBy(item => item.DueAt).Select(item => ToFollowUpResponse(item, item.OwnerUser)).ToList(),
            conversation.Assignments.OrderByDescending(item => item.AssignedAt).Select(ToAssignmentResponse).ToList(),
            conversation.Activities.OrderByDescending(item => item.CreatedAt).Select(ToActivityResponse).ToList(),
            conversation.Links.OrderByDescending(item => item.CreatedAt).Select(ToLinkResponse).ToList(),
            conversation.CreatedAt,
            conversation.UpdatedAt);
    }

    private static WhatsAppConnectionResponse? ToConnectionResponse(WhatsAppConnection? connection)
    {
        return connection is null
            ? null
            : new WhatsAppConnectionResponse(
                connection.Id,
                connection.Mode,
                connection.PhoneNumberId,
                connection.WabaId,
                connection.DisplayNumber,
                connection.Status,
                connection.BusinessPortfolioId,
                connection.EmbeddedSignupSessionId,
                connection.ExternalAccountId,
                connection.HistorySyncRequested,
                connection.Mode != WhatsAppConnectionMode.ManualOnly,
                connection.Mode != WhatsAppConnectionMode.ManualOnly,
                connection.Mode == WhatsAppConnectionMode.ManualOnly,
                BuildWaMeUrl(connection.DisplayNumber),
                connection.SecretReference is not null,
                connection.VerifiedAt,
                connection.ConnectedAt,
                connection.LastValidatedAt,
                connection.LastInboundAt,
                connection.LastOutboundAt,
                connection.CreatedAt,
                connection.UpdatedAt);
    }

    private static ContactResponse? ToContactResponse(Contact? contact)
    {
        return contact is null
            ? null
            : new ContactResponse(contact.Id, contact.NormalizedPhone, contact.DisplayName, contact.ProfileJson);
    }

    private static LeadResponse? ToLeadResponse(Lead? lead)
    {
        return lead is null
            ? null
            : new LeadResponse(lead.Id, lead.Status, lead.Source, lead.CampaignRef, lead.WonAt, lead.LostReason, lead.CreatedAt, lead.UpdatedAt);
    }

    private static ConversationMessageResponse ToMessageResponse(ConversationMessage message)
    {
        return new ConversationMessageResponse(
            message.Id,
            message.ProviderMessageId,
            message.Direction,
            message.Type,
            message.Body,
            message.Status,
            message.CreatedByUserId,
            message.CreatedByUser?.FullName,
            message.SentAt,
            message.DeliveredAt,
            message.ReadAt,
            message.FailedAt,
            message.FailureCode,
            message.CreatedAt,
            message.UpdatedAt);
    }

    private static InternalNoteResponse ToNoteResponse(InternalNote note, AppUser? author)
    {
        return new InternalNoteResponse(note.Id, note.AuthorUserId, author?.FullName, note.Body, note.CreatedAt);
    }

    private static FollowUpResponse ToFollowUpResponse(FollowUp followUp, AppUser? owner)
    {
        return new FollowUpResponse(
            followUp.Id,
            followUp.OwnerUserId,
            owner?.FullName,
            followUp.DueAt,
            followUp.Status,
            followUp.Note,
            followUp.CompletedAt,
            followUp.CancelledAt,
            followUp.CreatedAt,
            followUp.UpdatedAt);
    }

    private static ConversationAssignmentResponse ToAssignmentResponse(ConversationAssignment assignment)
    {
        return new ConversationAssignmentResponse(
            assignment.Id,
            assignment.FromUserId,
            assignment.ToUserId,
            assignment.ToUser?.FullName,
            assignment.AssignedTeamId,
            assignment.AssignedByUserId,
            assignment.Reason,
            assignment.AssignedAt);
    }

    private static ConversationActivityResponse ToActivityResponse(ConversationActivity activity)
    {
        return new ConversationActivityResponse(activity.Id, activity.Type, activity.ActorType, activity.ActorId, activity.MetadataJson, activity.CreatedAt);
    }

    private static ConversationLinkResponse ToLinkResponse(ConversationLink link)
    {
        return new ConversationLinkResponse(link.Id, link.LinkType, link.LinkedEntityId, link.ExternalReference, link.CreatedAt);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record ConversationAccess(Guid OrganizationId, IResult? Problem);
}

public sealed record ConversationListItemResponse(
    Guid Id,
    ConversationStatus Status,
    LeadStatus LeadStatus,
    Guid ConnectionId,
    string DisplayNumber,
    Guid ContactId,
    string NormalizedPhone,
    string? ContactName,
    Guid? AssignedUserId,
    string? AssignedUserName,
    Guid? AssignedTeamId,
    int UnreadCount,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? LastInboundMessageAt,
    DateTimeOffset? LastOutboundMessageAt,
    string? LastMessagePreview,
    DateTimeOffset UpdatedAt);

public sealed record ConversationDetailResponse(
    Guid Id,
    ConversationStatus Status,
    WhatsAppConnectionResponse? Connection,
    ContactResponse? Contact,
    LeadResponse? Lead,
    Guid? AssignedUserId,
    string? AssignedUserName,
    Guid? AssignedTeamId,
    int UnreadCount,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset? LastInboundMessageAt,
    DateTimeOffset? LastOutboundMessageAt,
    IReadOnlyList<ConversationMessageResponse> Messages,
    IReadOnlyList<InternalNoteResponse> Notes,
    IReadOnlyList<FollowUpResponse> FollowUps,
    IReadOnlyList<ConversationAssignmentResponse> Assignments,
    IReadOnlyList<ConversationActivityResponse> Activities,
    IReadOnlyList<ConversationLinkResponse> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WhatsAppConnectionResponse(
    Guid Id,
    WhatsAppConnectionMode Mode,
    string? PhoneNumberId,
    string? WabaId,
    string DisplayNumber,
    WhatsAppConnectionStatus Status,
    string? BusinessPortfolioId,
    string? EmbeddedSignupSessionId,
    string? ExternalAccountId,
    bool HistorySyncRequested,
    bool CanReceiveWebhooks,
    bool CanSendViaApi,
    bool RequiresExternalReply,
    string? ManualReplyUrl,
    bool HasSecretReference,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastValidatedAt,
    DateTimeOffset? LastInboundAt,
    DateTimeOffset? LastOutboundAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContactResponse(Guid Id, string NormalizedPhone, string? DisplayName, string ProfileJson);

public sealed record LeadResponse(
    Guid Id,
    LeadStatus Status,
    string Source,
    string? CampaignRef,
    DateTimeOffset? WonAt,
    string? LostReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConversationMessageResponse(
    Guid Id,
    string? ProviderMessageId,
    ConversationMessageDirection Direction,
    ConversationMessageType Type,
    string? Body,
    ConversationMessageStatus Status,
    Guid? CreatedByUserId,
    string? CreatedByUserName,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? FailedAt,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InternalNoteResponse(Guid Id, Guid AuthorUserId, string? AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record FollowUpResponse(
    Guid Id,
    Guid OwnerUserId,
    string? OwnerName,
    DateTimeOffset DueAt,
    ConversationFollowUpStatus Status,
    string? Note,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ConversationAssignmentResponse(
    Guid Id,
    Guid? FromUserId,
    Guid? ToUserId,
    string? ToUserName,
    Guid? AssignedTeamId,
    Guid AssignedByUserId,
    string? Reason,
    DateTimeOffset AssignedAt);

public sealed record ConversationActivityResponse(
    Guid Id,
    string Type,
    ActorType ActorType,
    string? ActorId,
    string MetadataJson,
    DateTimeOffset CreatedAt);

public sealed record ConversationLinkResponse(Guid Id, string LinkType, Guid? LinkedEntityId, string? ExternalReference, DateTimeOffset CreatedAt);

public sealed record AssignConversationRequest(Guid? UserId, Guid? TeamId, string? Reason);

public sealed record UnassignConversationRequest(string? Reason);

public sealed record CreateConversationMessageRequest(
    string? Body,
    ConversationMessageType? Type,
    string? TemplateName,
    string? ClientMessageId);

public sealed record CreateInternalNoteRequest(string? Body);

public sealed record CreateFollowUpRequest(Guid? OwnerUserId, DateTimeOffset DueAt, string? Note);

public sealed record UpdateLeadStatusRequest(LeadStatus Status, string? LostReason);

public sealed record ConversationAssignedResponse(Guid ConversationId, Guid? AssignedUserId, Guid? AssignedTeamId, LeadStatus LeadStatus);

public sealed record LeadStatusUpdatedResponse(Guid ConversationId, LeadStatus PreviousStatus, LeadStatus Status, ConversationStatus ConversationStatus);

public sealed record CreateManualConversationRequest(
    Guid ConnectionId,
    string? CustomerPhone,
    string? CustomerName,
    LeadStatus? LeadStatus,
    string? CampaignRef,
    Guid? AssignedUserId,
    Guid? AssignedTeamId,
    string? InitialNote);

public sealed record ManualConversationCreatedResponse(
    Guid ConversationId,
    Guid ConnectionId,
    Guid ContactId,
    Guid LeadId,
    string NormalizedPhone,
    string? ContactName,
    LeadStatus LeadStatus,
    Guid? AssignedUserId,
    Guid? AssignedTeamId,
    string? WaMeUrl,
    bool RequiresExternalReply);

public sealed record LogManualConversationMessageRequest(
    string? Body,
    DateTimeOffset? SentAt,
    string? ClientMessageId);

public sealed record WhatsAppOnboardingOptionsResponse(
    string WebhookCallbackUrl,
    IReadOnlyList<WhatsAppOnboardingModeResponse> Modes);

public sealed record WhatsAppOnboardingModeResponse(
    WhatsAppConnectionMode Mode,
    string Label,
    string Description,
    bool RequiresEmbeddedSignup,
    bool SupportsExistingBusinessApp,
    bool SupportsPersonalWhatsapp,
    bool CanReceiveWebhooks,
    bool CanSendViaApi,
    bool RequiresExternalReply,
    IReadOnlyList<string> RequiredCreateFields,
    string? UnsupportedReason);

public sealed record CreateWhatsAppConnectionRequest(
    WhatsAppConnectionMode? Mode,
    string? PhoneNumberId,
    string? WabaId,
    string? DisplayNumber,
    string? SecretReference,
    string? WebhookVerifyToken,
    string? BusinessPortfolioId,
    string? EmbeddedSignupSessionId,
    string? ExternalAccountId,
    bool? HistorySyncRequested,
    string? OnboardingSource,
    string? Notes);

public sealed record ConversationSummaryReportResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int NewLeads,
    int WonLeads,
    int LostLeads,
    int Conversations,
    int UnassignedConversations,
    int DueFollowUps,
    int IncomingMessages,
    int OutgoingMessages,
    IReadOnlyDictionary<string, int> LeadsByStatus);
