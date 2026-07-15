using Atlas.Api.Endpoints;
using Atlas.Api.Filters;
using Atlas.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-atlas_dashboard";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
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
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Project Atlas API",
        Version = "v1",
        Description = "Checklist, file intake, submission and admin configuration API."
    });
});
builder.Services.AddAtlasInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = context.TraceIdentifier;

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
app.UseAtlasSecurityHeaders();
app.UseAuthentication();
app.UseAuthorization();
app.UseAtlasTenantContext();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Project Atlas API v1");
    });
}

app.MapAtlasEndpoints();
app.MapAtlasSecurityEndpoints();
app.MapAtlasRecipientEndpoints();
app.MapAtlasSubmissionEndpoints();

app.Run();
