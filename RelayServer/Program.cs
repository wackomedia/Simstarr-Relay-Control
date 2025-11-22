using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/api/relay", async (HttpRequest req, HttpResponse res) =>
{
    using var sr = new StreamReader(req.Body);
    var body = await sr.ReadToEndAsync();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Received: {body}");
    res.StatusCode = 200;
    await res.WriteAsync("OK");
});

app.Run("http://0.0.0.0:5000");