using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
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
    rateLimiterOptions.AddPolicy(policyName: fixedPolicy, partitioner: httpContext =>
    {
        // Get client IP address
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, factory: _ =>
        {
            var myOptions = builder.Configuration
                .GetSection(MyRateLimitOptions.MyRateLimit)
                .Get<MyRateLimitOptions>();

            return new FixedWindowRateLimiterOptions
            {
                PermitLimit = myOptions.PermitLimit,
                Window = TimeSpan.FromMinutes(myOptions.Window),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = myOptions.QueueLimit
            };
        });
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

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors();
app.UseRateLimiter();

app.MapPost("api/replayfix", async (IFormFile file, ReplayFixHandler handler) =>
{
    try
    {
        await using var fileStream = file.OpenReadStream();
        var (stream, result) = await handler.Handle(fileStream);
        if (result is ReplayFixHandler.RepackResult.MissingProperty)
        {
            return TypedResults.Problem("The replay does not seem to be broken", statusCode: 400);
        }

        if (stream is null)
        {
            return TypedResults.Problem("Internal error: Stream is null");
        }
        return TypedResults.File(stream, "application/octet-stream", file.FileName);
    }
    catch (Exception ex)
    {
        return Results.Problem("Error processing replay");
    }
}).DisableAntiforgery()
    .RequireRateLimiting(fixedPolicy)
    .WithOpenApi();


app.Run();

public class ReplayFixHandler(ILogger<ReplayFixHandler> logger)
{
    public Task<(Stream? repackStream, RepackResult result)> Handle(Stream fileStream)
    {
        try
        {
            var binaryReader = new BinaryReader(fileStream);
            var replay = Replay.Deserialize(binaryReader);
            var index = replay.Objects.IndexOf("TAGame.PRI_TA:PlayerHistoryValid");
            if (index == -1)
            {
                logger.LogWarning("TAGame.PRI_TA:PlayerHistoryValid property not found. Someone uploaded a replay that doesn't seem broken.");
                return Task.FromResult<(Stream?, RepackResult)>((null, RepackResult.MissingProperty));
            }

            replay.Objects[index] = "TAGame.PRI_TA:bPlayerHistoryValid";
            var stream = new MemoryStream();
            replay.Serialize(stream);
            stream.Position = 0;
            logger.LogInformation("Successfully processed replay");
            return Task.FromResult<(Stream?, RepackResult)>((stream, RepackResult.Succes));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing replay");
            throw;
        }
    }
    
    

    public enum RepackResult
    {
        Succes, 
        MissingProperty
    }
}

public record MyRateLimitOptions
{
    public const string MyRateLimit = "MyRateLimit";
    public int PermitLimit { get; init; }
    public int Window { get; init; }
    public int QueueLimit { get; init; }
}