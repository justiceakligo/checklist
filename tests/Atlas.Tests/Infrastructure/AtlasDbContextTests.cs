using Atlas.Application.Abstractions;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atlas.Tests.Infrastructure;

public sealed class AtlasDbContextTests
{
    [Fact]
    public async Task Actions_are_filtered_to_current_tenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationA };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.Users.Add(new AppUser { Id = userId, Email = "owner@example.com", FullName = "Owner" });
            dbContext.Organizations.AddRange(
                new Organization { Id = organizationA, Name = "A", Slug = "a" },
                new Organization { Id = organizationB, Name = "B", Slug = "b" });
            dbContext.Actions.AddRange(
                new ChecklistAction
                {
                    OrganizationId = organizationA,
                    CreatedByUserId = userId,
                    PublicReference = "act_a",
                    Title = "Visible"
                },
                new ChecklistAction
                {
                    OrganizationId = organizationB,
                    CreatedByUserId = userId,
                    PublicReference = "act_b",
                    Title = "Hidden"
                });

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var actions = await dbContext.Actions.Select(action => action.Title).ToListAsync();
            Assert.Equal(["Visible"], actions);
        }

        tenant.OrganizationId = null;
        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            Assert.Empty(await dbContext.Actions.ToListAsync());
        }
    }

    [Fact]
    public async Task System_templates_are_visible_to_any_tenant()
    {
        var tenant = new TestTenantContext { OrganizationId = Guid.NewGuid() };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.Templates.Add(new Template
            {
                Name = "System template",
                Status = TemplateStatus.Published
            });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var template = await dbContext.Templates.SingleAsync();
            Assert.Null(template.OrganizationId);
            Assert.Equal("System template", template.Name);
        }
    }

    [Fact]
    public async Task Recipient_sessions_are_filtered_through_action_tenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationA };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.Users.Add(new AppUser { Id = userId, Email = "owner@example.com", FullName = "Owner" });
            dbContext.Organizations.AddRange(
                new Organization { Id = organizationA, Name = "A", Slug = "a" },
                new Organization { Id = organizationB, Name = "B", Slug = "b" });

            var actionA = new ChecklistAction
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                CreatedByUserId = userId,
                PublicReference = "act_a",
                Title = "A"
            };
            var recipientA = new ActionRecipient
            {
                Id = Guid.NewGuid(),
                Action = actionA,
                Name = "A",
                Email = "a@example.com",
                AccessTokenHash = [1]
            };
            var actionB = new ChecklistAction
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                CreatedByUserId = userId,
                PublicReference = "act_b",
                Title = "B"
            };
            var recipientB = new ActionRecipient
            {
                Id = Guid.NewGuid(),
                Action = actionB,
                Name = "B",
                Email = "b@example.com",
                AccessTokenHash = [2]
            };

            dbContext.AddRange(
                recipientA,
                recipientB,
                new RecipientAccessSession
                {
                    ActionRecipient = recipientA,
                    SessionTokenHash = [10],
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                },
                new RecipientAccessSession
                {
                    ActionRecipient = recipientB,
                    SessionTokenHash = [20],
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var sessions = await dbContext.RecipientAccessSessions.ToListAsync();
            Assert.Single(sessions);
            Assert.Equal([10], sessions[0].SessionTokenHash);
        }
    }

    [Fact]
    public async Task Idempotency_records_are_filtered_to_current_tenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationA };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.Organizations.AddRange(
                new Organization { Id = organizationA, Name = "A", Slug = "a" },
                new Organization { Id = organizationB, Name = "B", Slug = "b" });
            dbContext.IdempotencyRecords.AddRange(
                new IdempotencyRecord
                {
                    OrganizationId = organizationA,
                    Key = "same-key-a",
                    RequestHash = "hash-a",
                    StatusCode = 201,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
                },
                new IdempotencyRecord
                {
                    OrganizationId = organizationB,
                    Key = "same-key-b",
                    RequestHash = "hash-b",
                    StatusCode = 201,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var records = await dbContext.IdempotencyRecords.ToListAsync();
            Assert.Single(records);
            Assert.Equal("same-key-a", records[0].Key);
        }
    }

    [Fact]
    public async Task Conversation_records_are_filtered_to_current_tenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationA };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var connectionA = new WhatsAppConnection
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                PhoneNumberId = "phone-a",
                WabaId = "waba-a",
                DisplayNumber = "+15550000001"
            };
            var contactA = new Contact
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                ConnectionId = connectionA.Id,
                NormalizedPhone = "+15551111111"
            };
            var leadA = new Lead
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                ContactId = contactA.Id
            };
            var conversationA = new Conversation
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationA,
                ConnectionId = connectionA.Id,
                ContactId = contactA.Id,
                LeadId = leadA.Id
            };

            var connectionB = new WhatsAppConnection
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                PhoneNumberId = "phone-b",
                WabaId = "waba-b",
                DisplayNumber = "+15550000002"
            };
            var contactB = new Contact
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                ConnectionId = connectionB.Id,
                NormalizedPhone = "+15552222222"
            };
            var leadB = new Lead
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                ContactId = contactB.Id
            };
            var conversationB = new Conversation
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationB,
                ConnectionId = connectionB.Id,
                ContactId = contactB.Id,
                LeadId = leadB.Id
            };

            dbContext.Organizations.AddRange(
                new Organization { Id = organizationA, Name = "A", Slug = "a" },
                new Organization { Id = organizationB, Name = "B", Slug = "b" });
            dbContext.AddRange(connectionA, contactA, leadA, conversationA, connectionB, contactB, leadB, conversationB);
            dbContext.ConversationMessages.AddRange(
                new ConversationMessage
                {
                    OrganizationId = organizationA,
                    ConversationId = conversationA.Id,
                    Direction = ConversationMessageDirection.Incoming,
                    Status = ConversationMessageStatus.Received,
                    Body = "Visible"
                },
                new ConversationMessage
                {
                    OrganizationId = organizationB,
                    ConversationId = conversationB.Id,
                    Direction = ConversationMessageDirection.Incoming,
                    Status = ConversationMessageStatus.Received,
                    Body = "Hidden"
                });

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var conversations = await dbContext.Conversations.ToListAsync();
            var messages = await dbContext.ConversationMessages.ToListAsync();

            Assert.Single(conversations);
            Assert.Equal(organizationA, conversations[0].OrganizationId);
            Assert.Single(messages);
            Assert.Equal("Visible", messages[0].Body);
        }

        tenant.OrganizationId = null;
        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            Assert.Empty(await dbContext.Conversations.ToListAsync());
            Assert.Empty(await dbContext.ConversationMessages.ToListAsync());
        }
    }

    [Fact]
    public async Task Teams_are_filtered_to_current_tenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationA };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.Organizations.AddRange(
                new Organization { Id = organizationA, Name = "A", Slug = "a" },
                new Organization { Id = organizationB, Name = "B", Slug = "b" });
            dbContext.OrganizationTeams.AddRange(
                new OrganizationTeam
                {
                    OrganizationId = organizationA,
                    Name = "Sales"
                },
                new OrganizationTeam
                {
                    OrganizationId = organizationB,
                    Name = "Hidden Sales"
                });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            var teams = await dbContext.OrganizationTeams.ToListAsync();
            Assert.Single(teams);
            Assert.Equal("Sales", teams[0].Name);
        }

        tenant.OrganizationId = null;
        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            Assert.Empty(await dbContext.OrganizationTeams.ToListAsync());
        }
    }

    [Fact]
    public async Task Platform_administration_records_are_not_tenant_filtered()
    {
        var staffId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = Guid.NewGuid() };
        var databaseRoot = new InMemoryDatabaseRoot();

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            dbContext.PlatformStaff.Add(new PlatformStaff
            {
                Id = staffId,
                Email = "platform@example.com",
                FullName = "Platform Owner",
                Role = PlatformStaffRole.Owner,
                Status = PlatformStaffStatus.Active
            });
            dbContext.PlatformOrganizationInterests.Add(new PlatformOrganizationInterest
            {
                OrganizationName = "Interested Org",
                ContactName = "Jamie",
                ContactEmail = "jamie@example.com",
                Status = OrganizationInterestStatus.New,
                AssignedStaffId = staffId
            });
            dbContext.PlatformRevenueEvents.Add(new PlatformRevenueEvent
            {
                Type = PlatformRevenueEventType.Subscription,
                Amount = 99,
                Currency = "USD",
                Source = "manual",
                OccurredAt = DateTimeOffset.UtcNow,
                MetadataJson = "{}",
                RecordedByStaffId = staffId
            });
            dbContext.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                StaffId = staffId,
                EventType = "platform.test",
                EventData = "{}"
            });
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            Assert.Single(await dbContext.PlatformStaff.ToListAsync());
            Assert.Single(await dbContext.PlatformOrganizationInterests.ToListAsync());
            Assert.Single(await dbContext.PlatformRevenueEvents.ToListAsync());
            Assert.Single(await dbContext.PlatformAuditEvents.ToListAsync());
        }

        tenant.OrganizationId = null;
        await using (var dbContext = CreateContext(tenant, databaseRoot))
        {
            Assert.Single(await dbContext.PlatformStaff.ToListAsync());
            Assert.Single(await dbContext.PlatformOrganizationInterests.ToListAsync());
            Assert.Single(await dbContext.PlatformRevenueEvents.ToListAsync());
            Assert.Single(await dbContext.PlatformAuditEvents.ToListAsync());
        }
    }

    private static AtlasDbContext CreateContext(TestTenantContext tenantContext, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseInMemoryDatabase("atlas", databaseRoot)
            .Options;

        return new AtlasDbContext(options, tenantContext, new TestClock());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? OrganizationId { get; set; }
        public string? ActorId => "test";
        public string? ActorType => "user";
        public Guid? UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(["dashboard:*"], StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
