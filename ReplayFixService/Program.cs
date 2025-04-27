using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RocketRP;
using Scalar.AspNetCore;
using BinaryReader = RocketRP.BinaryReader;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition");
    });
});

builder.Services.Configure<MyRateLimitOptions>(
    builder.Configuration.GetSection(MyRateLimitOptions.MyRateLimit));
var fixedPolicy = "fixed";

builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.AddFixedWindowLimiter(policyName: fixedPolicy, options =>
    {
        var myOptions = builder.Configuration
            .GetSection(MyRateLimitOptions.MyRateLimit)
            .Get<MyRateLimitOptions>();
            
        options.PermitLimit = myOptions.PermitLimit;
        options.Window = TimeSpan.FromMinutes(myOptions.Window);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = myOptions.QueueLimit;
    });
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddSingleton<ReplayFixHandler>();

var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseRateLimiter();

app.MapPost("api/replayfix", async (IFormFile file, ReplayFixHandler handler) =>
{
    try
    {
        await using var fileStream = file.OpenReadStream();
        var data = await handler.Handle(fileStream);
        return TypedResults.File(data, "application/octet-stream", file.FileName);
    }
    catch (Exception ex)
    {
        return Results.Problem("Error processing replay");
    }
}).DisableAntiforgery().RequireRateLimiting(fixedPolicy);


app.Run();

public class ReplayFixHandler(ILogger<ReplayFixHandler> logger)
{
    public Task<byte[]> Handle(Stream fileStream)
    {
        try
        {
            var binaryReader = new BinaryReader(fileStream);
            var replay = Replay.Deserialize(binaryReader);
            var index = replay.Objects.IndexOf("TAGame.PRI_TA:PlayerHistoryValid");
            if (index != -1)
            {
                replay.Objects[index] = "TAGame.PRI_TA:bPlayerHistoryValid";
            }

            var replayBytes = replay.SerializeNetStream();
            return Task.FromResult(replayBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing replay");
            throw;
        }
    }
}

public record MyRateLimitOptions
{
    public const string MyRateLimit = "MyRateLimit";
    public int PermitLimit { get; init; }
    public int Window { get; init; }
    public int QueueLimit { get; init; }
}