using Microsoft.AspNetCore.Hosting;

namespace Notes.Tests.Integration;

/// <summary>When the background jobs run is a setting (<c>Config:Jobs</c>, docs/administration/jobs.md).</summary>
public sealed class JobScheduleTests
{
    [Theory]
    [InlineData("ClearStaleUploads")]
    [InlineData("CommitIdleChanges")]
    public void A_schedule_that_is_no_cron_expression_does_not_let_the_app_start_and_names_the_setting(string job)
    {
        using NotesAppFactory app = new();
        using var withSchedule = app.WithWebHostBuilder(builder => builder.UseSetting("Config:Jobs:" + job, "every now and then"));

        Exception refused = Assert.ThrowsAny<Exception>(() => withSchedule.CreateClient());

        Assert.Contains("Config:Jobs:" + job, refused.ToString());
    }

    [Fact]
    public void Another_schedule_lets_the_app_start()
    {
        using NotesAppFactory app = new();
        using var withSchedule = app.WithWebHostBuilder(builder => builder.UseSetting("Config:Jobs:CommitIdleChanges", "*/5 * * * *"));

        _ = withSchedule.CreateClient();
    }
}
