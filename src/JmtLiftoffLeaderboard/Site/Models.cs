using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// The JSON shapes the JMT site's public read endpoints answer with. Field for field the
// pydantic schemas in the site's api/app/schemas/timing.py and catalogue.py; anything
// the site adds later is ignored, and anything it drops arrives as its default.
namespace JmtLiftoffLeaderboard.Site;

public sealed class PilotRef
{
    [JsonProperty("public_id")] public string PublicId = "";
    [JsonProperty("display_name")] public string DisplayName = "";
    [JsonProperty("steam_id")] public string? SteamId;
    [JsonProperty("persona_name")] public string? PersonaName;
    [JsonProperty("avatar_url")] public string? AvatarUrl;
    [JsonProperty("profile_url")] public string? ProfileUrl;

    /// <summary>Linked to a Steam account. The site marks everyone else as a guest.</summary>
    public bool IsClaimed => !string.IsNullOrEmpty(SteamId);

    public string Name =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
        : !string.IsNullOrWhiteSpace(PersonaName) ? PersonaName!
        : "Unknown pilot";
}

public sealed class LeaderboardRow
{
    [JsonProperty("position")] public int Position;
    [JsonProperty("pilot")] public PilotRef Pilot = new();
    [JsonProperty("lap_ms")] public int LapMs;
    [JsonProperty("set_at")] public DateTimeOffset SetAt;
    [JsonProperty("laps")] public int Laps;
    [JsonProperty("movement")] public int? Movement;
    [JsonProperty("movement_since")] public DateTimeOffset? MovementSince;
    [JsonProperty("first_lap_ms")] public int? FirstLapMs;
    [JsonProperty("improvement_ms")] public int? ImprovementMs;
    [JsonProperty("consistency_ms")] public int? ConsistencyMs;
}

public sealed class RecentLap
{
    [JsonProperty("pilot")] public PilotRef Pilot = new();
    [JsonProperty("lap_ms")] public int LapMs;
    [JsonProperty("recorded_at")] public DateTimeOffset RecordedAt;
    [JsonProperty("is_personal_best")] public bool IsPersonalBest;
    [JsonProperty("delta_ms")] public int? DeltaMs;
}

public sealed class Leaderboard
{
    [JsonProperty("published_file_id")] public long? PublishedFileId;
    [JsonProperty("title")] public string Title = "";
    /// <summary>"workshop", "stock" (one of Liftoff's own tracks) or "unlinked". The last two have negative ids.</summary>
    [JsonProperty("kind")] public string Kind = "workshop";
    [JsonProperty("environment")] public string? Environment;
    [JsonProperty("environment_display")] public string? EnvironmentDisplay;
    [JsonProperty("pilot_count")] public int PilotCount;
    [JsonProperty("lap_count")] public int LapCount;
    [JsonProperty("match_count")] public int MatchCount;
    [JsonProperty("rows")] public List<LeaderboardRow> Rows = new();
    [JsonProperty("fastest_lap_ms")] public int? FastestLapMs;
    [JsonProperty("leader")] public PilotRef? Leader;
    [JsonProperty("median_lap_ms")] public int? MedianLapMs;
    [JsonProperty("pbs_today")] public int PbsToday;
    [JsonProperty("updated_at")] public DateTimeOffset? UpdatedAt;
    [JsonProperty("recent")] public List<RecentLap> Recent = new();
}

public sealed class BoardSummary
{
    [JsonProperty("published_file_id")] public long PublishedFileId;
    [JsonProperty("title")] public string Title = "";
    [JsonProperty("kind")] public string Kind = "workshop";
    [JsonProperty("content_type")] public string? ContentType;
    [JsonProperty("environment")] public string? Environment;
    [JsonProperty("environment_display")] public string? EnvironmentDisplay;
    [JsonProperty("preview_url")] public string? PreviewUrl;
    [JsonProperty("pilot_count")] public int PilotCount;
    [JsonProperty("lap_count")] public int LapCount;
    [JsonProperty("fastest_lap_ms")] public int? FastestLapMs;
    [JsonProperty("last_lap_at")] public DateTimeOffset? LastLapAt;
}

public sealed class BoardPage
{
    [JsonProperty("items")] public List<BoardSummary> Items = new();
    [JsonProperty("total")] public int Total;
    [JsonProperty("limit")] public int Limit;
    [JsonProperty("offset")] public int Offset;
}

public sealed class PilotTrackRow
{
    [JsonProperty("published_file_id")] public long PublishedFileId;
    [JsonProperty("title")] public string Title = "";
    [JsonProperty("kind")] public string Kind = "workshop";
    [JsonProperty("lap_ms")] public int LapMs;
    [JsonProperty("set_at")] public DateTimeOffset SetAt;
    [JsonProperty("laps")] public int Laps;
    [JsonProperty("position")] public int Position;
    [JsonProperty("pilot_count")] public int PilotCount;
}

public sealed class PilotProfile
{
    [JsonProperty("pilot")] public PilotRef Pilot = new();
    [JsonProperty("first_seen_at")] public DateTimeOffset FirstSeenAt;
    [JsonProperty("last_seen_at")] public DateTimeOffset LastSeenAt;
    [JsonProperty("total_laps")] public int TotalLaps;
    [JsonProperty("track_count")] public int TrackCount;
    [JsonProperty("tracks")] public List<PilotTrackRow> Tracks = new();

    [JsonProperty("pace_top_pct")] public double? PaceTopPct;
    [JsonProperty("pace_boards")] public int PaceBoards;

    [JsonProperty("jr_rating")] public int? JrRating;
    [JsonProperty("jr_peak_rating")] public int? JrPeakRating;
    [JsonProperty("jr_races")] public int JrRaces;
    [JsonProperty("jr_provisional")] public bool JrProvisional;
    [JsonProperty("jr_rank")] public int? JrRank;
    [JsonProperty("jr_field")] public int JrField;
    [JsonProperty("jr_movement")] public int? JrMovement;
    [JsonProperty("jr_placement_races")] public int JrPlacementRaces;
    [JsonProperty("jr_division")] public string? JrDivision;
    [JsonProperty("jr_next_division")] public string? JrNextDivision;
    [JsonProperty("jr_next_division_at")] public int? JrNextDivisionAt;

    [JsonProperty("cr_rating")] public double? CrRating;
    [JsonProperty("cr_failed")] public int CrFailed;
    [JsonProperty("cr_laps")] public int CrLaps;
    [JsonProperty("cr_license")] public string? CrLicense;
    [JsonProperty("cr_next_license")] public string? CrNextLicense;
    [JsonProperty("cr_next_license_at")] public double? CrNextLicenseAt;
    [JsonProperty("cr_next_license_laps")] public int? CrNextLicenseLaps;

    [JsonProperty("live_room")] public string? LiveRoom;
}

public sealed class LicenseRung
{
    [JsonProperty("name")] public string Name = "";
    [JsonProperty("floor")] public double Floor;
}

/// <summary>
/// The numbers a Consistency Rating is worked out with. The site sends its own with every
/// consistency answer, and they are due to be tuned; these defaults are what it used when
/// this version was written, for an answer that doesn't carry them.
/// </summary>
public sealed class CrModel
{
    [JsonProperty("max")] public double Max = 5.0;
    [JsonProperty("midpoint_fpl")] public double MidpointFpl = 2.0;
    [JsonProperty("prior_laps")] public int PriorLaps = 10;
    [JsonProperty("window_laps")] public int WindowLaps = 100;
    [JsonProperty("license_min_laps")] public int LicenseMinLaps = 25;
    [JsonProperty("pro_min_laps")] public int ProMinLaps = 100;

    /// <summary>Highest first. Replaced by the site's, not added to: Newtonsoft appends to a list it finds already filled.</summary>
    [JsonProperty("licenses", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<LicenseRung> Licenses = new()
    {
        new() { Name = "Pro", Floor = 4.0 },
        new() { Name = "A", Floor = 3.5 },
        new() { Name = "B", Floor = 3.0 },
        new() { Name = "C", Floor = 2.0 },
        new() { Name = "D", Floor = 0.0 },
    };

    /// <summary>A reset is a failed attempt when the attempt it abandoned ran this long or more...</summary>
    [JsonProperty("too_short_ms")] public int TooShortMs = 5_000;
    /// <summary>...and no longer than this.</summary>
    [JsonProperty("idle_ms")] public int IdleMs = 120_000;
}

/// <summary>One race inside a pilot's CR window.</summary>
public sealed class CrRace
{
    [JsonProperty("failed")] public int Failed;
    [JsonProperty("laps")] public int Laps;
}

/// <summary>A pilot's CR, the races it was taken over (newest first), and the numbers behind it.</summary>
public sealed class PilotConsistency
{
    [JsonProperty("pilot")] public PilotRef Pilot = new();
    [JsonProperty("cr_rating")] public double? CrRating;
    [JsonProperty("cr_failed")] public int CrFailed;
    [JsonProperty("cr_laps")] public int CrLaps;
    [JsonProperty("cr_license")] public string? CrLicense;
    [JsonProperty("races", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<CrRace> Races = new();
    [JsonProperty("model")] public CrModel Model = new();
}

/// <summary>A room being flown right now, as the site's live page lists it.</summary>
public sealed class LivePanel
{
    [JsonProperty("in_room")] public bool InRoom;
    [JsonProperty("room_name")] public string RoomName = "";
    [JsonProperty("pilots")] public List<PilotRef> Pilots = new();
}

public sealed class ItemCreator
{
    [JsonProperty("persona_name")] public string? PersonaName;
}

/// <summary>A workshop item from the catalogue: what a board's header shows about its course.</summary>
public sealed class ItemInfo
{
    [JsonProperty("published_file_id")] public long PublishedFileId;
    [JsonProperty("title")] public string Title = "";
    [JsonProperty("preview_url")] public string? PreviewUrl;
    [JsonProperty("content_type")] public string? ContentType;
    [JsonProperty("environment_display")] public string? EnvironmentDisplay;
    [JsonProperty("creator")] public ItemCreator? Creator;
}
