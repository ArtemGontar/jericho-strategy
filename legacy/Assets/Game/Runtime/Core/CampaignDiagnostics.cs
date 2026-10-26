using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Nasledie.Core
{
    /// <summary>Stable hashes for saved campaign state and the rules data used to run it.</summary>
    public static class CampaignFingerprint
    {
        private static readonly string ruleHash = ComputeRules();

        public static string State(GameState state)
        {
            return StableObjectHash.Compute(state, null);
        }

        public static string StateWithoutTurnHashes(GameState state)
        {
            return StableObjectHash.Compute(state, "TurnStateHashes");
        }

        public static string TurnState(GameState state)
        {
            return StableObjectHash.Compute(state, "TurnStateHashes");
        }

        public static string Rules()
        {
            return ruleHash;
        }

        private static string ComputeRules()
        {
            return StableObjectHash.Compute(new object[]
            {
                RulesCatalog.Technologies,
                RulesCatalog.Buildings,
                RulesCatalog.Improvements,
                RulesCatalog.Units,
                RulesCatalog.Laws,
                RulesCatalog.Events,
                NationCatalog.All
            }, null);
        }
    }

    internal static class StableObjectHash
    {
        private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        public static string Compute(object value, string ignoredRootField)
        {
            var builder = new StringBuilder(32768);
            Append(builder, value, ignoredRootField, true);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var result = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) result.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static void Append(StringBuilder builder, object value, string ignoredRootField, bool isRoot)
        {
            if (value == null) { builder.Append("null;"); return; }
            Type type = value.GetType();
            builder.Append(type.FullName).Append(':');
            if (value is string)
            {
                string text = (string)value;
                builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text).Append(';');
                return;
            }
            if (type.IsEnum || type.IsPrimitive || value is decimal)
            {
                IFormattable formatted = value as IFormattable;
                string format = value is float || value is double ? "R" : null;
                builder.Append(formatted == null ? value.ToString() : formatted.ToString(format, CultureInfo.InvariantCulture)).Append(';');
                return;
            }
            Array array = value as Array;
            if (array != null)
            {
                builder.Append('[').Append(array.Length.ToString(CultureInfo.InvariantCulture)).Append(':');
                for (int i = 0; i < array.Length; i++) Append(builder, array.GetValue(i), null, false);
                builder.Append("]; ");
                return;
            }

            FieldInfo[] fields;
            if (!fieldCache.TryGetValue(type, out fields))
            {
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
                Array.Sort(fields, delegate(FieldInfo first, FieldInfo second) { return string.CompareOrdinal(first.Name, second.Name); });
                fieldCache[type] = fields;
            }
            int fieldCount = fields.Length;
            if (isRoot && !string.IsNullOrEmpty(ignoredRootField))
                for (int i = 0; i < fields.Length; i++) if (fields[i].Name == ignoredRootField) { fieldCount--; break; }
            builder.Append('{').Append(fieldCount.ToString(CultureInfo.InvariantCulture)).Append(':');
            for (int i = 0; i < fields.Length; i++)
            {
                if (isRoot && fields[i].Name == ignoredRootField) continue;
                builder.Append(fields[i].Name).Append('=');
                Append(builder, fields[i].GetValue(value), null, false);
            }
            builder.Append("};");
        }
    }

    public sealed class CampaignReplayResult
    {
        public readonly bool Success;
        public readonly string Message;
        public readonly string ExpectedHash;
        public readonly string ReplayHash;

        public CampaignReplayResult(bool success, string message, string expectedHash, string replayHash)
        {
            Success = success;
            Message = message ?? string.Empty;
            ExpectedHash = expectedHash ?? string.Empty;
            ReplayHash = replayHash ?? string.Empty;
        }
    }

    /// <summary>Rebuilds a campaign from its seed and accepted player commands, then verifies the full state hash.</summary>
    public static class CampaignReplay
    {
        public static CampaignReplayResult Verify(GameState expected)
        {
            if (expected == null) return new CampaignReplayResult(false, "Нет состояния кампании.", string.Empty, string.Empty);
            if (expected.CommandJournalTruncated)
                return new CampaignReplayResult(false, "Журнал команд усечён; полный повтор кампании невозможен.", CampaignFingerprint.State(expected), string.Empty);
            if (expected.CommandJournal == null)
                return new CampaignReplayResult(false, "В сохранении отсутствует журнал команд.", CampaignFingerprint.State(expected), string.Empty);
            CivilizationState player = expected.Civilization(expected.HumanCivilizationId);
            if (player == null) return new CampaignReplayResult(false, "В сохранении не найден игрок.", CampaignFingerprint.State(expected), string.Empty);

            string currentRulesHash = CampaignFingerprint.Rules();
            if (!string.IsNullOrEmpty(expected.RulesetHash) && expected.RulesetHash != currentRulesHash)
                return new CampaignReplayResult(false, "Хэш правил сохранения отличается от текущего набора правил.", CampaignFingerprint.State(expected), string.Empty);

            GameSimulation replay = GameSimulation.NewCampaign(expected.Seed, player.Difficulty, expected.Pace, player.NationId);
            int commandIndex = 0;
            for (int turn = 1; turn <= expected.Turn; turn++)
            {
                if (replay.State.IsFinished) break;
                while (commandIndex < expected.CommandJournal.Length)
                {
                    ReplayCommand command;
                    string error;
                    if (!TryParse(expected.CommandJournal[commandIndex], out command, out error))
                        return new CampaignReplayResult(false, "Журнал повреждён: " + error, CampaignFingerprint.State(expected), string.Empty);
                    if (command.Turn < turn)
                        return new CampaignReplayResult(false, "В журнале нарушен порядок ходов.", CampaignFingerprint.State(expected), string.Empty);
                    if (command.Turn > turn) break;
                    CommandResult result = replay.Issue(command.Command);
                    if (!result.Accepted)
                        return new CampaignReplayResult(false, "Команда не повторилась на ходу " + turn + ": " + result.Code + " · " + result.Message,
                            CampaignFingerprint.State(expected), CampaignFingerprint.State(replay.State));
                    commandIndex++;
                }

                bool needsTurnResolution = turn < expected.Turn || expected.IsFinished;
                if (needsTurnResolution)
                {
                    replay.EndTurn();
                    if (replay.State.IsFinished) break;
                }
            }

            if (commandIndex != expected.CommandJournal.Length)
                return new CampaignReplayResult(false, "В журнале остались команды за пределами сохранённого хода.",
                    CampaignFingerprint.State(expected), CampaignFingerprint.State(replay.State));
            bool hasTurnCheckpoints = expected.TurnStateHashes != null && expected.TurnStateHashes.Length > 0;
            if (hasTurnCheckpoints && !SameTurnCheckpoints(expected.TurnStateHashes, replay.State.TurnStateHashes))
                return new CampaignReplayResult(false, "Повтор расходится с контрольным хэшем одного из завершённых ходов.",
                    CampaignFingerprint.State(expected), CampaignFingerprint.State(replay.State));
            string expectedHash = hasTurnCheckpoints ? CampaignFingerprint.State(expected) : CampaignFingerprint.StateWithoutTurnHashes(expected);
            string replayHash = hasTurnCheckpoints ? CampaignFingerprint.State(replay.State) : CampaignFingerprint.StateWithoutTurnHashes(replay.State);
            bool matches = string.Equals(expectedHash, replayHash, StringComparison.Ordinal);
            return new CampaignReplayResult(matches,
                matches ? (hasTurnCheckpoints ? "Повтор успешен: состояние и контрольные хэши ходов совпали." : "Повтор успешен: состояние совпало; сохранение не содержит исторических хэшей ходов.")
                    : "Повтор завершился, но состояние отличается.",
                expectedHash, replayHash);
        }

        private static bool SameTurnCheckpoints(string[] expected, string[] replayed)
        {
            if (expected == null) expected = Array.Empty<string>();
            if (replayed == null) replayed = Array.Empty<string>();
            if (expected.Length != replayed.Length) return false;
            for (int i = 0; i < expected.Length; i++)
                if (!string.Equals(expected[i], replayed[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private sealed class ReplayCommand
        {
            public int Turn;
            public GameCommand Command;
        }

        private static bool TryParse(string entry, out ReplayCommand replayCommand, out string error)
        {
            replayCommand = null;
            error = "неизвестная запись";
            if (string.IsNullOrEmpty(entry)) return false;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] parts = entry.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int separator = parts[i].IndexOf('=');
                if (separator <= 0) continue;
                fields[parts[i].Substring(0, separator)] = parts[i].Substring(separator + 1);
            }
            int turn, actor, entity, target, value, sequence;
            string[] cell = fields.ContainsKey("cell") ? fields["cell"].Split(',') : new string[0];
            int q, r;
            CommandKind kind;
            if (!TryInt(fields, "turn", out turn) || !TryInt(fields, "actor", out actor) || !TryInt(fields, "entity", out entity) ||
                !TryInt(fields, "target", out target) || !TryInt(fields, "value", out value) || !TryInt(fields, "sequence", out sequence) ||
                cell.Length != 2 || !int.TryParse(cell[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out q) ||
                !int.TryParse(cell[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out r) || !fields.ContainsKey("kind") ||
                !Enum.TryParse(fields["kind"], false, out kind))
            {
                error = "не удалось прочитать поля команды";
                return false;
            }
            replayCommand = new ReplayCommand
            {
                Turn = turn,
                Command = new GameCommand
                {
                    Kind = kind, ActorId = actor, EntityId = entity, TargetId = target, Q = q, R = r,
                    Value = value, Sequence = sequence,
                    Text = fields.ContainsKey("text") ? fields["text"] : string.Empty,
                    SecondaryText = fields.ContainsKey("secondary") ? fields["secondary"] : string.Empty
                }
            };
            error = string.Empty;
            return true;
        }

        private static bool TryInt(Dictionary<string, string> fields, string key, out int value)
        {
            value = 0;
            return fields.ContainsKey(key) && int.TryParse(fields[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
