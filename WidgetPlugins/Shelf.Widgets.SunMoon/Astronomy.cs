using System;

namespace Shelf.Widgets.SunMoon;

// Self-contained astronomical math: sunrise/sunset (and twilight/golden/blue
// thresholds) via the standard NOAA solar-position algorithm, plus Moon phase,
// illumination and rise/set via Meeus low-precision formulae. Accuracy is ~1-2
// minutes - more than enough for a desktop widget, and crucially needs no network
// or API key (only the city -> lat/lon geocode is online, done once upstream).
//
// Sign conventions: latitude north-positive, longitude EAST-positive (matches
// Open-Meteo). All public results are returned as UTC DateTimes; the widget
// converts to the city's timezone for display.
internal static class Astronomy
{
    public const double SynodicMonth = 29.530588853; // mean days between new moons

    private static double Rad(double deg) => deg * Math.PI / 180.0;
    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    private static double Norm360(double a)
    {
        a %= 360.0;
        return a < 0 ? a + 360.0 : a;
    }

    // Signed normalize to (-180, 180].
    private static double Norm180(double a)
    {
        a = Norm360(a);
        return a > 180.0 ? a - 360.0 : a;
    }

    // Julian Day at 0h UT for a calendar date (Gregorian).
    private static double JulianDay(int year, int month, int day)
    {
        if (month <= 2) { year -= 1; month += 12; }
        int a = year / 100;
        int b = 2 - a + a / 4;
        return Math.Floor(365.25 * (year + 4716))
               + Math.Floor(30.6001 * (month + 1))
               + day + b - 1524.5;
    }

    private static double JulianDay(DateTime utc)
    {
        double dayFraction = (utc.Hour + utc.Minute / 60.0 + utc.Second / 3600.0) / 24.0;
        return JulianDay(utc.Year, utc.Month, utc.Day) + dayFraction;
    }

    // ===== Sun =====

    // Equation of time (minutes) and solar declination (radians) for a given JD,
    // using the NOAA/Meeus low-precision series.
    private static (double eqTimeMin, double declRad) SolarParams(double jd)
    {
        double t = (jd - 2451545.0) / 36525.0;

        double l0 = Norm360(280.46646 + t * (36000.76983 + 0.0003032 * t));
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double mRad = Rad(m);
        double e = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        double c = (1.914602 - t * (0.004817 + 0.000014 * t)) * Math.Sin(mRad)
                   + (0.019993 - 0.000101 * t) * Math.Sin(2 * mRad)
                   + 0.000289 * Math.Sin(3 * mRad);

        double trueLong = l0 + c;
        double omega = 125.04 - 1934.136 * t;
        double appLong = trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(omega));

        double eps0 = 23.0 + (26.0 + ((21.448 - t * (46.815 + t * (0.00059 - t * 0.001813)))) / 60.0) / 60.0;
        double eps = eps0 + 0.00256 * Math.Cos(Rad(omega));
        double epsRad = Rad(eps);

        double decl = Math.Asin(Math.Sin(epsRad) * Math.Sin(Rad(appLong)));

        double y = Math.Tan(epsRad / 2.0);
        y *= y;
        double l0Rad = Rad(l0);
        double eqTime = 4.0 * Deg(
            y * Math.Sin(2 * l0Rad)
            - 2 * e * Math.Sin(mRad)
            + 4 * e * y * Math.Sin(mRad) * Math.Cos(2 * l0Rad)
            - 0.5 * y * y * Math.Sin(4 * l0Rad)
            - 1.25 * e * e * Math.Sin(2 * mRad));

        return (eqTime, decl);
    }

    // UTC instant when the Sun reaches the given zenith angle on `date`, either
    // ascending (rising=true) or descending. Null when the Sun never reaches that
    // altitude that day (polar day/night for the chosen threshold).
    //
    // Common zeniths: 90.833 = sunrise/sunset (refraction + disc radius),
    // 96 = civil, 102 = nautical, 108 = astronomical twilight. Golden/blue hour
    // use custom altitudes mapped to zenith = 90 - altitude.
    public static DateTime? SunEventUtc(DateOnly date, double lat, double lonEast,
                                        double zenithDeg, bool rising)
    {
        double jd = JulianDay(date.Year, date.Month, date.Day);
        var (eqTime, decl) = SolarParams(jd + 0.5); // evaluate near mid-day
        double latRad = Rad(lat);

        double cosH = (Math.Cos(Rad(zenithDeg)) - Math.Sin(decl) * Math.Sin(latRad))
                      / (Math.Cos(decl) * Math.Cos(latRad));
        if (cosH > 1.0 || cosH < -1.0) return null; // no such event this day

        double hDeg = Deg(Math.Acos(cosH));         // half-arc, degrees
        double noonMinUtc = 720.0 - 4.0 * lonEast - eqTime;
        double eventMin = rising ? noonMinUtc - 4.0 * hDeg : noonMinUtc + 4.0 * hDeg;

        var midnightUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        return midnightUtc.AddMinutes(eventMin);
    }

    // UTC instant of solar noon on `date`.
    public static DateTime SolarNoonUtc(DateOnly date, double lonEast)
    {
        double jd = JulianDay(date.Year, date.Month, date.Day);
        var (eqTime, _) = SolarParams(jd + 0.5);
        double noonMinUtc = 720.0 - 4.0 * lonEast - eqTime;
        var midnightUtc = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        return midnightUtc.AddMinutes(noonMinUtc);
    }

    public enum PolarState { Normal, PolarDay, PolarNight }

    // Distinguishes a missing sunrise as polar day (Sun always up) vs polar night
    // (Sun always down), by checking the Sun's altitude at solar noon.
    public static PolarState GetPolarState(DateOnly date, double lat, double lonEast)
    {
        double jd = JulianDay(date.Year, date.Month, date.Day);
        var (_, decl) = SolarParams(jd + 0.5);
        double latRad = Rad(lat);
        double noonAlt = Deg(Math.Asin(
            Math.Sin(latRad) * Math.Sin(decl) + Math.Cos(latRad) * Math.Cos(decl)));
        // Sun's max altitude for the day; -0.833 is the geometric sunrise altitude.
        return noonAlt < -0.833 ? PolarState.PolarNight : PolarState.PolarDay;
    }

    // ===== Moon =====

    // Illuminated fraction (0..1), age in days, 8-phase index and waxing flag for
    // the given UTC moment.
    public static (double illum, double ageDays, int phaseIndex, bool waxing) MoonPhase(DateTime utc)
    {
        double jd = JulianDay(utc);
        double elong = MoonElongation(jd);

        double illum = (1.0 - Math.Cos(Rad(elong))) / 2.0;
        bool waxing = elong < 180.0;
        double age = SynodicMonth * elong / 360.0;
        int idx = PhaseIndexFromElongation(elong);
        return (illum, age, idx, waxing);
    }

    // Moon-Sun ecliptic-longitude difference (degrees, 0..360). 0 = new, 180 = full.
    private static double MoonElongation(double jd)
    {
        double d = jd - 2451545.0;

        double sunMeanAnom = Rad(357.529 + 0.98560028 * d);
        double sunMeanLong = 280.459 + 0.98564736 * d;
        double sunLong = sunMeanLong + 1.915 * Math.Sin(sunMeanAnom) + 0.020 * Math.Sin(2 * sunMeanAnom);

        double moonMeanLong = 218.316 + 13.176396 * d;
        double moonMeanAnom = Rad(134.963 + 13.064993 * d);
        double moonLong = moonMeanLong + 6.289 * Math.Sin(moonMeanAnom);

        return Norm360(moonLong - sunLong);
    }

    private static int PhaseIndexFromElongation(double elong)
    {
        // 8 buckets, each 45 deg wide, centered on the 4 principal phases.
        // 0 New, 1 Waxing Crescent, 2 First Quarter, 3 Waxing Gibbous,
        // 4 Full, 5 Waning Gibbous, 6 Last Quarter, 7 Waning Crescent.
        return (int)(Math.Floor(Norm360(elong + 22.5) / 45.0)) % 8;
    }

    // Next UTC instant the Moon reaches a target elongation (0 = new, 180 = full)
    // after `fromUtc`. Newton iteration on the mean motion - converges to minutes.
    public static DateTime NextMoonPhase(DateTime fromUtc, double targetElong)
    {
        const double meanRate = 12.190749; // deg/day, Moon relative to Sun
        double jd = JulianDay(fromUtc);
        double elongNow = MoonElongation(jd);

        double diff = Norm360(targetElong - elongNow);
        if (diff < 0.1) diff += 360.0; // strictly the *next* one, not "now"
        DateTime t = fromUtc.AddDays(diff / meanRate);

        for (int i = 0; i < 6; i++)
        {
            double e = MoonElongation(JulianDay(t));
            double err = Norm180(targetElong - e);
            t = t.AddDays(err / meanRate);
        }
        return t;
    }

    // Geocentric Moon altitude (degrees) at a UTC instant for lat/lonEast.
    private static double MoonAltitude(DateTime utc, double lat, double lonEast)
    {
        double jd = JulianDay(utc);
        double d = jd - 2451545.0;

        double moonMeanLong = 218.316 + 13.176396 * d;
        double moonMeanAnom = Rad(134.963 + 13.064993 * d);
        double moonArgLat = Rad(93.272 + 13.229350 * d);

        double lonEcl = moonMeanLong + 6.289 * Math.Sin(moonMeanAnom);
        double latEcl = 5.128 * Math.Sin(moonArgLat);
        double eps = 23.439 - 0.0000004 * d;

        double lonRad = Rad(lonEcl);
        double latRad = Rad(latEcl);
        double epsRad = Rad(eps);

        double raRad = Math.Atan2(
            Math.Sin(lonRad) * Math.Cos(epsRad) - Math.Tan(latRad) * Math.Sin(epsRad),
            Math.Cos(lonRad));
        double decRad = Math.Asin(
            Math.Sin(latRad) * Math.Cos(epsRad) + Math.Cos(latRad) * Math.Sin(epsRad) * Math.Sin(lonRad));

        double gmst = 280.46061837 + 360.98564736629 * d; // degrees
        double lst = gmst + lonEast;
        double hRad = Rad(lst - Deg(raRad));

        double obsLatRad = Rad(lat);
        double alt = Math.Asin(
            Math.Sin(obsLatRad) * Math.Sin(decRad)
            + Math.Cos(obsLatRad) * Math.Cos(decRad) * Math.Cos(hRad));
        return Deg(alt);
    }

    // Moonrise/moonset as UTC instants within the local day that spans
    // [localMidnightUtc, localMidnightUtc + 24h]. Either may be null (the Moon can
    // skip a rise or set on a given calendar day). Found by sampling altitude every
    // 10 minutes and linearly interpolating the horizon crossing (h0 = +0.125,
    // mean parallax minus refraction).
    public static (DateTime? rise, DateTime? set) MoonRiseSet(DateTime localMidnightUtc, double lat, double lonEast)
    {
        const double h0 = 0.125;
        const int stepMin = 10;
        const int steps = 24 * 60 / stepMin;

        DateTime? rise = null, set = null;
        double prevAlt = MoonAltitude(localMidnightUtc, lat, lonEast) - h0;

        for (int i = 1; i <= steps; i++)
        {
            DateTime t = localMidnightUtc.AddMinutes(i * stepMin);
            double alt = MoonAltitude(t, lat, lonEast) - h0;

            if (prevAlt < 0 && alt >= 0 && rise == null)
            {
                double frac = prevAlt / (prevAlt - alt);
                rise = t.AddMinutes(-stepMin * (1 - frac));
            }
            else if (prevAlt >= 0 && alt < 0 && set == null)
            {
                double frac = prevAlt / (prevAlt - alt);
                set = t.AddMinutes(-stepMin * (1 - frac));
            }
            prevAlt = alt;
        }
        return (rise, set);
    }
}
