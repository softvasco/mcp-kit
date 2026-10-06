using McpKit;
using McpKit.Sample;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMcpKit().WithTools<ClockTools>();

var app = builder.Build();
app.MapMcpKit();
app.Run();

// lets the tests host this app with WebApplicationFactory
public partial class Program;
