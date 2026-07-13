using Aegis.Audit;
using Aegis.Dashboard.Components;
using Aegis.Dashboard.Services;
using Aegis.Sql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Aegis"));
builder.Services.AddSingleton<PolicyBrowserService>();
builder.Services.AddSingleton<RelationshipBrowserService>();

var auditLogConnectionString = builder.Configuration["Aegis:AuditLog:ConnectionString"];
if (!string.IsNullOrWhiteSpace(auditLogConnectionString))
{
    builder.Services.AddSingleton<IAuditLogStore>(
        new SqlAuditLogStore(new SqlAuditLogStoreOptions { ConnectionString = auditLogConnectionString }));
    builder.Services.AddSingleton(new AuditLogStatus(IsPersistent: true));
}
else
{
    builder.Services.AddSingleton<IAuditLogStore>(new InMemoryAuditLogStore());
    builder.Services.AddSingleton(new AuditLogStatus(IsPersistent: false));
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();