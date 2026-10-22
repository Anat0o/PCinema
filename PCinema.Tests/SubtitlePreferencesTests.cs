using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;

public sealed class SubtitlePreferencesTests
{
    [Fact]
    public void OffSurvivesRestartAndEnablingAnotherTrackRestoresDefault()
    {
        var dir=Path.Combine(Path.GetTempPath(),"PCinema-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var first=new SubtitlePreferences(new LibraryStore(dir));
            Assert.False(first.Disabled);
            first.SelectTrack(-1);
            var nextEpisode=new SubtitlePreferences(new LibraryStore(dir));
            Assert.True(nextEpisode.Disabled);
            nextEpisode.SelectTrack(4);
            Assert.False(new SubtitlePreferences(new LibraryStore(dir)).Disabled);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(dir,true); }
    }
}
