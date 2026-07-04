using Scalar.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Notes;
using CommandLine;
[assembly: ApiController]

Options? options = null;
Parser.Default.ParseArguments<Options>(args)
    .WithParsed(o =>
    {
        options = o;
    });
if (options == null)
{
    Console.WriteLine("Failed to parse command line arguments.");
    return;
}

if (string.IsNullOrEmpty(options.Directory))
{
    Console.Write("Enter the entry point directory: ");
    options.Directory = Console.ReadLine()!;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Make the parsed command-line options available via DI.
builder.Services.AddSingleton(options!);
builder.Services.AddSingleton<Notes.Core.Disk.Data.IService, Notes.Core.Disk.Data.Service>();
builder.Services.AddSingleton<Notes.Core.Disk.Directory.IService, Notes.Core.Disk.Directory.Service>();
builder.Services.AddSingleton<Notes.Core.Disk.Trash.IService, Notes.Core.Disk.Trash.Service>();
builder.Services.AddSingleton<Notes.Core.Disk.Config.IService, Notes.Core.Disk.Config.Service>();
builder.Services.AddSingleton<Notes.Core.Disk.Naming.IService, Notes.Core.Disk.Naming.Service>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();

WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // http://localhost:5162/scalar/
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
