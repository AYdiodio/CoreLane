using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CoreAllocator
{
    internal static class SteamLaunchAffinity
    {
        private const string ApexLibrarySuffix = @"\steamapps\common\Apex Legends";
        internal static bool IsApex(ProcessRow row)
        {
            return string.Equals(row.Name, "r5apex.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.Name, "r5apex_dx12.exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static ProcessRow FindLauncher(IEnumerable<ProcessRow> rows, ProcessRow game)
        {
            if (!IsApex(game)) return null;
            var verified = new List<ProcessRow>();
            foreach (ProcessRow steam in rows.Where(row => string.Equals(row.Name, "steam.exe", StringComparison.OrdinalIgnoreCase) &&
                SavedApplication.ValidPath(row.ExecutablePath) && row.ReadError == null && row.Mask != 0 && row.Created < game.Created))
            {
                try
                {
                    string path = Path.Combine(Path.GetDirectoryName(steam.ExecutablePath), "logs", "gameprocess_log.txt");
                    using (var log = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (log.Length > 256 * 1024) log.Seek(-256 * 1024, SeekOrigin.End);
                        using (var reader = new StreamReader(log))
                            if (ConfirmsLaunch(reader, game, steam.Created)) verified.Add(steam);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return verified.Count == 1 ? verified[0] : null;
        }

        internal static bool ConfirmsLaunch(TextReader log, ProcessRow game, long steamCreated)
        {
            if (!IsApex(game) || steamCreated >= game.Created) return false;
            string gameDirectory = SavedApplication.ValidPath(game.ExecutablePath) ? Path.GetDirectoryName(game.ExecutablePath) : null;
            if (gameDirectory != null && !gameDirectory.EndsWith(ApexLibrarySuffix, StringComparison.OrdinalIgnoreCase)) return false;
            DateTime gameStarted = DateTime.FromFileTime(game.Created);
            DateTime steamStarted = DateTime.FromFileTime(steamCreated);
            string tracked = "AppID 1172470 adding PID " + game.Pid.ToString(CultureInfo.InvariantCulture) + " as a tracked process";
            string removed = "AppID 1172470 no longer tracking PID " + game.Pid.ToString(CultureInfo.InvariantCulture) + ",";
            bool launcher = false, match = false;
            string line;
            while ((line = log.ReadLine()) != null)
            {
                DateTime timestamp;
                if (line.Length < 22 || line[0] != '[' || line[20] != ']' ||
                    !DateTime.TryParseExact(line.Substring(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp) ||
                    timestamp < steamStarted.AddSeconds(-1)) continue;
                string entry = line.Substring(22);
                if (entry.StartsWith("Remove 1172470 from running list", StringComparison.Ordinal)) { launcher = false; match = false; }
                if (entry.StartsWith(removed, StringComparison.Ordinal)) match = false;
                if (!entry.StartsWith("AppID 1172470 adding PID ", StringComparison.Ordinal)) continue;
                int quote = entry.IndexOf('"');
                if (quote >= 0 && timestamp <= gameStarted.AddSeconds(2))
                {
                    string command = entry.Substring(quote);
                    string expected = (gameDirectory ?? ApexLibrarySuffix) + @"\EAAntiCheat.GameServiceLauncher.exe";
                    launcher = command.IndexOf(expected + "\" -steam", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (launcher && string.Equals(entry.TrimEnd(), tracked, StringComparison.Ordinal) &&
                    Math.Abs((timestamp - gameStarted).TotalSeconds) <= 10) match = true;
            }
            return match;
        }
    }
}
