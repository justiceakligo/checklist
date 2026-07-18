using Atlas.Api.Endpoints;
using Atlas.Api.Filters;
using Atlas.Api.Services;
using Atlas.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
const string PermissiveCorsPolicy = "PermissiveCors";

builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = true;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-atlas_dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddCors(options =>
{
    options.AddPolicy(PermissiveCorsPolicy, policy =>
    {
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });
});
builder.Services.AddAuthorization();
builder.Services.AddDataProtection();
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var forwardedFor = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
        var key = string.IsNullOrWhiteSpace(forwardedFor)
            ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : forwardedFor;

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await Results.Problem(
            title: "Too many requests.",
            statusCode: StatusCodes.Status429TooManyRequests,
            type: "https://docs.atlas.example/errors/rate_limited",
            extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
            .ExecuteAsync(context.HttpContext);
    };
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Reqara API",
        Version = "v1",
        Description = "Checklist, file intake, submission and admin configuration API."
    });
});
builder.Services.AddAtlasInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ReminderDispatcherService>();
builder.Services.AddHostedService<RetentionPurgeService>();
builder.Services.AddHostedService<FileScanDispatcherService>();
builder.Services.AddHostedService<DeliveryJobDispatcherService>();

var app = builder.Build();

app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = context.TraceIdentifier;
        if (exception is BadHttpRequestException badRequest)
        {
            await Results.Problem(
                title: "Invalid request body.",
                detail: "The request body could not be parsed or did not match the expected JSON contract.",
                statusCode: badRequest.StatusCode,
                type: "https://docs.atlas.example/errors/invalid_request_body",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "invalid_request_body",
                    ["traceId"] = traceId,
                    ["errors"] = new[] { "Check Content-Type, JSON syntax, and required request fields." }
                }).ExecuteAsync(context);
            return;
        }

        await Results.Problem(
            title: "An unexpected error occurred.",
            detail: app.Environment.IsDevelopment() ? exception?.Message : null,
            statusCode: StatusCodes.Status500InternalServerError,
            type: "https://docs.atlas.example/errors/internal_error",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "internal_error",
                ["traceId"] = traceId
            }).ExecuteAsync(context);
    });
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors(PermissiveCorsPolicy);
app.UseRateLimiter();
app.UseAtlasSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseAtlasTenantContext();

app.UseSwagger(options =>
{
    options.RouteTemplate = "openapi/{documentName}.json";
});
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Reqara API v1");
    });
}

app.MapAtlasEndpoints();
app.MapAtlasSecurityEndpoints();
app.MapAtlasRecipientEndpoints();
app.MapAtlasSubmissionEndpoints();
app.MapAtlasPackageEndpoints();
app.MapAtlasPlatformEndpoints();
app.MapAtlasAnalyticsEndpoints();

app.Run();
