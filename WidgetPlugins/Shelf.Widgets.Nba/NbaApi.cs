using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shelf.Widgets.Nba;

// Thin async wrapper over ESPN's public JSON endpoints for NBA. No API key, no
// auth. Parses into the simple POCO models below; resilient to missing fields
// (ESPN occasionally restructures payloads) - returns null/defaults instead of
// throwing on shape mismatches.
public static class NbaApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string Base = "https://site.api.espn.com/apis/site/v2/sports/basketball/nba";

    // ===== Models =====

    public sealed class GameSummary
    {
        public string EventId { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public string Status { get; set; } = "";          // "STATUS_FINAL" / "STATUS_IN_PROGRESS" / "STATUS_SCHEDULED" / etc.
        public string StatusDetail { get; set; } = "";    // human-readable: "Final", "Q3 04:21", "7:30 PM ET"
        public string HomeAbbrev { get; set; } = "";
        public string AwayAbbrev { get; set; } = "";
        public string HomeLogoUrl { get; set; } = "";
        public string AwayLogoUrl { get; set; } = "";
        public int? HomeScore { get; set; }
        public int? AwayScore { get; set; }
        public string Venue { get; set; } = "";
        public string SeriesSummary { get; set; } = "";   // e.g. "BOS leads series 2-1"
        public string SeasonType { get; set; } = "";      // "2" regular, "3" postseason
        public string TopScorerName { get; set; } = "";   // populated only by full GameDetails fetch
        public int TopScorerPoints { get; set; }

        public bool IsFinal => Status?.Contains("FINAL", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsLive => Status?.Contains("IN_PROGRESS", StringComparison.OrdinalIgnoreCase) == true
                              || Status?.Contains("HALFTIME", StringComparison.OrdinalIgnoreCase) == true;
    }

    public sealed class GameDetails
    {
        public GameSummary Summary { get; set; } = new();
        public List<int> HomeLineScore { get; set; } = new();
        public List<int> AwayLineScore { get; set; } = new();
        public List<PlayerLine> HomeTopPlayers { get; set; } = new();
        public List<PlayerLine> AwayTopPlayers { get; set; } = new();
        public string EspnGameUrl { get; set; } = "";
    }

    public sealed class PlayerLine
    {
        public string Name { get; set; } = "";
        public int Points { get; set; }
        public int Rebounds { get; set; }
        public int Assists { get; set; }
    }

    // ===== Endpoints =====

    // /scoreboard?dates=YYYYMMDD - all NBA games for the given date.
    // postseasonOnly=true adds &seasontype=3 (playoffs); false leaves it default
    // (whatever ESPN's current season state is).
    public static async Task<List<GameSummary>> GetScoreboardAsync(DateTime localDate, bool postseasonOnly = false)
    {
        string ymd = localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string url = $"{Base}/scoreboard?dates={ymd}";
        if (postseasonOnly) url += "&seasontype=3";

        var games = new List<GameSummary>();
        try
        {
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("events", out var events)
                || events.ValueKind != JsonValueKind.Array) return games;

            foreach (var ev in events.EnumerateArray())
            {
                var g = ParseEvent(ev);
                if (g != null) games.Add(g);
            }
        }
        catch
        {
            // Network / parse failure - return whatever we have so far (possibly empty).
        }
        return games;
    }

    // /summary?event={id} - full box score for a single game. Returns null on failure.
    public static async Task<GameDetails?> GetGameDetailsAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return null;
        string url = $"{Base}/summary?event={Uri.EscapeDataString(eventId)}";
        try
        {
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            return ParseSummary(doc.RootElement, eventId);
        }
        catch
        {
            return null;
        }
    }

    // Convenience: pulls the most recent completed game and up to <paramref name="nextCount"/>
    // upcoming games for an abbreviation by scanning a window of dates around "now".
    // Lightweight alternative to ESPN's team-schedule endpoint which has flaky pagination.
    public static async Task<(GameSummary? last, List<GameSummary> nexts)> GetTeamLastAndNextsAsync(
        string abbrev, DateTime now, int nextCount)
    {
        string norm = NbaTeams.Normalize(abbrev);
        GameSummary? last = null;
        var nexts = new List<GameSummary>();
        if (nextCount < 1) nextCount = 1;

        // Wider forward window to accommodate up to 5 upcoming games.
        for (int offset = -14; offset <= 21; offset++)
        {
            var games = await GetScoreboardAsync(now.Date.AddDays(offset));
            foreach (var g in games)
            {
                bool involved = string.Equals(g.HomeAbbrev, norm, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(g.AwayAbbrev, norm, StringComparison.OrdinalIgnoreCase);
                if (!involved) continue;

                if (g.IsFinal && g.StartUtc <= now)
                {
                    if (last == null || g.StartUtc > last.StartUtc) last = g;
                }
                else if (!g.IsFinal && g.StartUtc >= now.AddHours(-3))
                {
                    nexts.Add(g);
                }
            }
        }
        nexts.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
        if (nexts.Count > nextCount) nexts = nexts.GetRange(0, nextCount);
        return (last, nexts);
    }

    // ESPN puts the primary team logo at either competitors[].team.logo or in
    // a richer competitors[].team.logos[] array (e.g. light vs dark variants).
    // We pick the first http(s) URL we find.
    private static string ExtractLogoUrl(JsonElement teamElement)
    {
        if (teamElement.TryGetProperty("logo", out var l)
            && l.GetString() is { } single
            && (single.StartsWith("http://") || single.StartsWith("https://")))
            return single;

        if (teamElement.TryGetProperty("logos", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.TryGetProperty("href", out var h)
                    && h.GetString() is { } u
                    && (u.StartsWith("http://") || u.StartsWith("https://")))
                    return u;
            }
        }
        return "";
    }

    // ===== Parsing helpers =====

    private static GameSummary? ParseEvent(JsonElement ev)
    {
        try
        {
            var g = new GameSummary
            {
                EventId = ev.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            };

            if (ev.TryGetProperty("date", out var dt) && dt.GetString() is { } s &&
                DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            {
                g.StartUtc = utc;
            }

            if (ev.TryGetProperty("season", out var season) && season.TryGetProperty("type", out var st))
                g.SeasonType = st.ValueKind == JsonValueKind.Number ? st.GetInt32().ToString(CultureInfo.InvariantCulture) : (st.GetString() ?? "");

            if (!ev.TryGetProperty("competitions", out var comps)
                || comps.ValueKind != JsonValueKind.Array
                || comps.GetArrayLength() == 0) return g;

            var comp = comps[0];

            if (comp.TryGetProperty("status", out var status) && status.TryGetProperty("type", out var stype))
            {
                g.Status = stype.TryGetProperty("name", out var sname) ? sname.GetString() ?? "" : "";
                g.StatusDetail = stype.TryGetProperty("shortDetail", out var sd) ? sd.GetString() ?? "" : "";
            }

            if (comp.TryGetProperty("venue", out var venue) && venue.TryGetProperty("fullName", out var vn))
                g.Venue = vn.GetString() ?? "";

            // series.summary lives on the competition for playoff games:
            //   "series": { "summary": "GS leads series 2-1" }
            if (comp.TryGetProperty("series", out var series) && series.TryGetProperty("summary", out var ssm))
                g.SeriesSummary = ssm.GetString() ?? "";

            if (comp.TryGetProperty("competitors", out var teams) && teams.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in teams.EnumerateArray())
                {
                    string side = t.TryGetProperty("homeAway", out var ha) ? (ha.GetString() ?? "") : "";
                    string abbr = "";
                    string logo = "";
                    if (t.TryGetProperty("team", out var team))
                    {
                        if (team.TryGetProperty("abbreviation", out var ab))
                            abbr = NbaTeams.Normalize(ab.GetString());
                        logo = ExtractLogoUrl(team);
                    }
                    int? score = null;
                    if (t.TryGetProperty("score", out var sc))
                    {
                        if (sc.ValueKind == JsonValueKind.Number) score = sc.GetInt32();
                        else if (sc.ValueKind == JsonValueKind.String && int.TryParse(sc.GetString(), out var sci)) score = sci;
                    }

                    if (string.Equals(side, "home", StringComparison.OrdinalIgnoreCase))
                    {
                        g.HomeAbbrev = abbr; g.HomeScore = score; g.HomeLogoUrl = logo;
                    }
                    else if (string.Equals(side, "away", StringComparison.OrdinalIgnoreCase))
                    {
                        g.AwayAbbrev = abbr; g.AwayScore = score; g.AwayLogoUrl = logo;
                    }
                }
            }

            // Try to grab a "top scorer" leader if the scoreboard payload carries it
            // (it usually does for finals).
            if (comp.TryGetProperty("competitors", out var teams2) && teams2.ValueKind == JsonValueKind.Array)
            {
                int bestPts = -1;
                string bestName = "";
                foreach (var t in teams2.EnumerateArray())
                {
                    if (!t.TryGetProperty("leaders", out var leaders) || leaders.ValueKind != JsonValueKind.Array) continue;
                    foreach (var ld in leaders.EnumerateArray())
                    {
                        if (!ld.TryGetProperty("name", out var lname) || lname.GetString() != "points") continue;
                        if (!ld.TryGetProperty("leaders", out var lds) || lds.ValueKind != JsonValueKind.Array || lds.GetArrayLength() == 0) continue;
                        var first = lds[0];
                        int pts = 0;
                        if (first.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                            pts = (int)Math.Round(v.GetDouble());
                        string nm = "";
                        if (first.TryGetProperty("athlete", out var ath) && ath.TryGetProperty("displayName", out var dn))
                            nm = dn.GetString() ?? "";
                        if (pts > bestPts) { bestPts = pts; bestName = nm; }
                    }
                }
                if (bestPts >= 0)
                {
                    g.TopScorerName = bestName;
                    g.TopScorerPoints = bestPts;
                }
            }

            return g;
        }
        catch
        {
            return null;
        }
    }

    private static GameDetails? ParseSummary(JsonElement root, string eventId)
    {
        try
        {
            var det = new GameDetails();
            string awayAbbr = "", homeAbbr = "";

            // header.competitions[0] has scores, status, lineScores
            if (root.TryGetProperty("header", out var header)
                && header.TryGetProperty("competitions", out var comps)
                && comps.ValueKind == JsonValueKind.Array
                && comps.GetArrayLength() > 0)
            {
                var comp = comps[0];

                det.Summary.EventId = eventId;
                if (comp.TryGetProperty("date", out var dt) && dt.GetString() is { } s &&
                    DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                {
                    det.Summary.StartUtc = utc;
                }

                if (comp.TryGetProperty("status", out var status) && status.TryGetProperty("type", out var stype))
                {
                    det.Summary.Status = stype.TryGetProperty("name", out var sname) ? sname.GetString() ?? "" : "";
                    det.Summary.StatusDetail = stype.TryGetProperty("shortDetail", out var sd) ? sd.GetString() ?? "" : "";
                }

                if (comp.TryGetProperty("competitors", out var teams) && teams.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in teams.EnumerateArray())
                    {
                        string side = t.TryGetProperty("homeAway", out var ha) ? (ha.GetString() ?? "") : "";
                        string abbr = "";
                        string logo = "";
                        if (t.TryGetProperty("team", out var team))
                        {
                            if (team.TryGetProperty("abbreviation", out var ab))
                                abbr = NbaTeams.Normalize(ab.GetString());
                            logo = ExtractLogoUrl(team);
                        }

                        int? score = null;
                        if (t.TryGetProperty("score", out var sc))
                        {
                            if (sc.ValueKind == JsonValueKind.Number) score = sc.GetInt32();
                            else if (sc.ValueKind == JsonValueKind.String && int.TryParse(sc.GetString(), out var sci)) score = sci;
                        }

                        var lineScore = new List<int>();
                        if (t.TryGetProperty("linescores", out var ls) && ls.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var q in ls.EnumerateArray())
                            {
                                if (q.TryGetProperty("value", out var qv) && qv.ValueKind == JsonValueKind.Number)
                                    lineScore.Add((int)Math.Round(qv.GetDouble()));
                            }
                        }

                        bool isHome = string.Equals(side, "home", StringComparison.OrdinalIgnoreCase);
                        if (isHome)
                        {
                            det.Summary.HomeAbbrev = abbr; det.Summary.HomeScore = score; det.HomeLineScore = lineScore;
                            det.Summary.HomeLogoUrl = logo;
                            homeAbbr = abbr;
                        }
                        else
                        {
                            det.Summary.AwayAbbrev = abbr; det.Summary.AwayScore = score; det.AwayLineScore = lineScore;
                            det.Summary.AwayLogoUrl = logo;
                            awayAbbr = abbr;
                        }
                    }
                }
            }

            if (root.TryGetProperty("gameInfo", out var info)
                && info.TryGetProperty("venue", out var venue)
                && venue.TryGetProperty("fullName", out var vn))
                det.Summary.Venue = vn.GetString() ?? "";

            // boxscore.players[].statistics[].athletes - top scorers by team
            if (root.TryGetProperty("boxscore", out var box)
                && box.TryGetProperty("players", out var players)
                && players.ValueKind == JsonValueKind.Array)
            {
                foreach (var teamPlayers in players.EnumerateArray())
                {
                    string abbr = "";
                    if (teamPlayers.TryGetProperty("team", out var tm) && tm.TryGetProperty("abbreviation", out var ab))
                        abbr = NbaTeams.Normalize(ab.GetString());

                    var top = ExtractTopThreePlayers(teamPlayers);
                    if (string.Equals(abbr, homeAbbr, StringComparison.OrdinalIgnoreCase))
                        det.HomeTopPlayers = top;
                    else if (string.Equals(abbr, awayAbbr, StringComparison.OrdinalIgnoreCase))
                        det.AwayTopPlayers = top;
                }
            }

            // header.links[] holds the canonical game page URL.
            if (root.TryGetProperty("header", out var hdr) && hdr.TryGetProperty("links", out var links)
                && links.ValueKind == JsonValueKind.Array)
            {
                foreach (var lk in links.EnumerateArray())
                {
                    if (lk.TryGetProperty("href", out var href) && href.GetString() is { } hv && hv.StartsWith("http"))
                    {
                        det.EspnGameUrl = hv;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(det.EspnGameUrl))
                det.EspnGameUrl = $"https://www.espn.com/nba/game/_/gameId/{eventId}";

            return det;
        }
        catch
        {
            return null;
        }
    }

    private static List<PlayerLine> ExtractTopThreePlayers(JsonElement teamPlayers)
    {
        var lines = new List<PlayerLine>();
        if (!teamPlayers.TryGetProperty("statistics", out var stats) || stats.ValueKind != JsonValueKind.Array)
            return lines;

        foreach (var stat in stats.EnumerateArray())
        {
            if (!stat.TryGetProperty("athletes", out var athletes) || athletes.ValueKind != JsonValueKind.Array)
                continue;

            // ESPN gives keys[] describing column meanings; we look up pts/reb/ast by name.
            var keys = new List<string>();
            if (stat.TryGetProperty("keys", out var keysArr) && keysArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in keysArr.EnumerateArray())
                    if (k.GetString() is { } ks) keys.Add(ks);
            }
            int idxPts = keys.IndexOf("points"); if (idxPts < 0) idxPts = keys.IndexOf("pts");
            int idxReb = keys.IndexOf("rebounds"); if (idxReb < 0) idxReb = keys.IndexOf("reb");
            int idxAst = keys.IndexOf("assists"); if (idxAst < 0) idxAst = keys.IndexOf("ast");

            foreach (var a in athletes.EnumerateArray())
            {
                string name = "";
                if (a.TryGetProperty("athlete", out var ath) && ath.TryGetProperty("displayName", out var dn))
                    name = dn.GetString() ?? "";

                if (!a.TryGetProperty("stats", out var sarr) || sarr.ValueKind != JsonValueKind.Array) continue;
                var values = new List<string>();
                foreach (var v in sarr.EnumerateArray()) values.Add(v.GetString() ?? "0");

                int pts = ReadStatValue(values, idxPts);
                int reb = ReadStatValue(values, idxReb);
                int ast = ReadStatValue(values, idxAst);

                if (pts > 0 || reb > 0 || ast > 0)
                    lines.Add(new PlayerLine { Name = name, Points = pts, Rebounds = reb, Assists = ast });
            }
        }

        lines.Sort((x, y) => y.Points.CompareTo(x.Points));
        if (lines.Count > 3) lines = lines.GetRange(0, 3);
        return lines;
    }

    private static int ReadStatValue(List<string> values, int index)
    {
        if (index < 0 || index >= values.Count) return 0;
        return int.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 0;
    }
}
