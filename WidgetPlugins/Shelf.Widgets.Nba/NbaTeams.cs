using System.Collections.Generic;
using System.Linq;

namespace Shelf.Widgets.Nba;

// Static catalog of NBA franchises. Used by the settings dialog (favourite team
// dropdown) and by Api / Widget code to render a friendly name from an ESPN
// abbreviation when the API payload only carries the short form.
public static class NbaTeams
{
    public sealed class Team
    {
        public string Abbreviation { get; init; } = "";
        public string FullName { get; init; } = "";
        public string Conference { get; init; } = ""; // "East" or "West"
    }

    public static readonly IReadOnlyList<Team> All = new[]
    {
        // Eastern Conference
        new Team { Abbreviation = "ATL", FullName = "Atlanta Hawks",         Conference = "East" },
        new Team { Abbreviation = "BOS", FullName = "Boston Celtics",        Conference = "East" },
        new Team { Abbreviation = "BKN", FullName = "Brooklyn Nets",         Conference = "East" },
        new Team { Abbreviation = "CHA", FullName = "Charlotte Hornets",     Conference = "East" },
        new Team { Abbreviation = "CHI", FullName = "Chicago Bulls",         Conference = "East" },
        new Team { Abbreviation = "CLE", FullName = "Cleveland Cavaliers",   Conference = "East" },
        new Team { Abbreviation = "DET", FullName = "Detroit Pistons",       Conference = "East" },
        new Team { Abbreviation = "IND", FullName = "Indiana Pacers",        Conference = "East" },
        new Team { Abbreviation = "MIA", FullName = "Miami Heat",            Conference = "East" },
        new Team { Abbreviation = "MIL", FullName = "Milwaukee Bucks",       Conference = "East" },
        new Team { Abbreviation = "NY",  FullName = "New York Knicks",       Conference = "East" },
        new Team { Abbreviation = "ORL", FullName = "Orlando Magic",         Conference = "East" },
        new Team { Abbreviation = "PHI", FullName = "Philadelphia 76ers",    Conference = "East" },
        new Team { Abbreviation = "TOR", FullName = "Toronto Raptors",       Conference = "East" },
        new Team { Abbreviation = "WSH", FullName = "Washington Wizards",    Conference = "East" },

        // Western Conference
        new Team { Abbreviation = "DAL", FullName = "Dallas Mavericks",      Conference = "West" },
        new Team { Abbreviation = "DEN", FullName = "Denver Nuggets",        Conference = "West" },
        new Team { Abbreviation = "GS",  FullName = "Golden State Warriors", Conference = "West" },
        new Team { Abbreviation = "HOU", FullName = "Houston Rockets",       Conference = "West" },
        new Team { Abbreviation = "LAC", FullName = "LA Clippers",           Conference = "West" },
        new Team { Abbreviation = "LAL", FullName = "Los Angeles Lakers",    Conference = "West" },
        new Team { Abbreviation = "MEM", FullName = "Memphis Grizzlies",     Conference = "West" },
        new Team { Abbreviation = "MIN", FullName = "Minnesota Timberwolves",Conference = "West" },
        new Team { Abbreviation = "NO",  FullName = "New Orleans Pelicans",  Conference = "West" },
        new Team { Abbreviation = "OKC", FullName = "Oklahoma City Thunder", Conference = "West" },
        new Team { Abbreviation = "PHX", FullName = "Phoenix Suns",          Conference = "West" },
        new Team { Abbreviation = "POR", FullName = "Portland Trail Blazers",Conference = "West" },
        new Team { Abbreviation = "SAC", FullName = "Sacramento Kings",      Conference = "West" },
        new Team { Abbreviation = "SA",  FullName = "San Antonio Spurs",     Conference = "West" },
        new Team { Abbreviation = "UTAH",FullName = "Utah Jazz",             Conference = "West" },
    };

    public static Team? Find(string? abbreviation)
    {
        if (string.IsNullOrWhiteSpace(abbreviation)) return null;
        return All.FirstOrDefault(t =>
            string.Equals(t.Abbreviation, abbreviation, System.StringComparison.OrdinalIgnoreCase));
    }

    // ESPN sometimes returns slightly different abbreviations (e.g. "GSW" vs "GS",
    // "NYK" vs "NY"). Normalise to the catalog form on the way in so lookups work.
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string a = raw.Trim().ToUpperInvariant();
        return a switch
        {
            "GSW" => "GS",
            "NYK" => "NY",
            "NOP" => "NO",
            "SAS" => "SA",
            "UTA" => "UTAH",
            "WAS" => "WSH",
            _ => a
        };
    }

    public static string DisplayName(string? abbreviation)
    {
        var t = Find(Normalize(abbreviation));
        return t?.FullName ?? (abbreviation ?? "");
    }
}
