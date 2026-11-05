using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>A signal face that changes between custom LED signs by itself.</summary>
    public sealed class FaceMessages
    {
        public int Head;
        public bool Minus;
        public byte Seconds = 10;
        public bool OnlyHeavy;       // shows only when traffic is heavy; otherwise the face shows its own sign
        public readonly List<ushort> Ids = new List<ushort>();

        public FaceMessages Copy()
        {
            var c = new FaceMessages { Head = Head, Minus = Minus, Seconds = Seconds, OnlyHeavy = OnlyHeavy };
            c.Ids.AddRange(Ids);
            return c;
        }

        public bool Same(FaceMessages o)
        {
            if (Head != o.Head || Minus != o.Minus || Seconds != o.Seconds || OnlyHeavy != o.OnlyHeavy || Ids.Count != o.Ids.Count)
            {
                return false;
            }

            for (int i = 0; i < Ids.Count; i++)
            {
                if (Ids[i] != o.Ids[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Signals that follow what's happening: speed limits that drop in heavy traffic, and faces that change between
    /// messages. What the player set stays as it is underneath; this only changes what's shown, so turning it off, or
    /// opening the city in a version without it, leaves the signal as it was. Traffic itself isn't changed.
    ///
    /// Format, key "GantryTool.Live": byte version, int count, then per gantry: int id, vectors start and end (stale-data
    /// guard), bool liveSpeed, int messages, per message: int head, bool minus, byte seconds, bool onlyHeavy, byte count, ushort ids.
    /// </summary>
    public static class LiveSigns
    {
        public const string Key = "GantryTool.Live";
        private const byte Version = 1;
        private const int MaxMessages = 64;
        public const int MaxPerFace = 8;
        private const float TrafficEvery = 5f, TickEvery = 1f;
        private const int LowestKmh = 30;
        public static readonly byte[] SecondsChoices = { 5, 10, 20, 60 };

        private static float _next, _nextTraffic;
        private static int _editGantry, _editSign;
        private static float _editUntil;

        /// <summary>The time signs go by, kept still between looks so everything drawn together agrees.</summary>
        public static float PageClock { get; private set; }

        /// <summary>The sign editor says what's open: that sign shows the message being edited, not the next in turn.</summary>
        public static void Editing(int gantry, int sign)
        {
            _editGantry = gantry;
            _editSign = sign;
            _editUntil = Time.realtimeSinceStartup + 0.5f;
        }

        public static bool IsEditing(int gantry, int sign)
        {
            return gantry == _editGantry && sign == _editSign && Time.realtimeSinceStartup < _editUntil;
        }

        private static bool HasChangingSign(Gantry g)
        {
            foreach (GuideHead h in g.GuideSigns)
            {
                if (GantryTool.Guide.GuidePages.Changes(h.Design))
                {
                    return true;
                }
            }

            return false;
        }
        private static readonly List<Gantry> Gantries = new List<Gantry>();

        /// <summary>
        /// How far to lower the limit at a road's traffic density, the game's own 0-100 measure of how full a road is, not how
        /// fast it's moving: none to three steps, starting from the density that counts as heavy.
        /// </summary>
        public static int StepFor(int density, int heavy = 50)
        {
            return density < heavy ? 0 : density < heavy + 20 ? 1 : density < heavy + 35 ? 2 : 3;
        }

        public static int Lowered(int kmh, int step)
        {
            return step <= 0 || kmh <= LowestKmh ? kmh : Mathf.Max(LowestKmh, kmh - 20 * step);
        }

        public static FaceMessages Find(Gantry g, int head, bool minus)
        {
            foreach (FaceMessages m in g.Messages)
            {
                if (m.Head == head && m.Minus == minus)
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>What a face shows now, given its own sign, the traffic step, and the time in seconds.</summary>
        public static FaceSign Show(Gantry g, int head, bool minus, FaceSign own, int step, float now)
        {
            FaceMessages m = Find(g, head, minus);
            if (m != null && m.Ids.Count > 0 && (!m.OnlyHeavy || step > 0))
            {
                int at = (int)(now / Mathf.Max(1, (int)m.Seconds)) % m.Ids.Count;
                return new FaceSign(SignalSymbol.Custom, m.Ids[at]);
            }

            if (g.LiveSpeed && own.Symbol == SignalSymbol.SpeedLimit)
            {
                return new FaceSign(SignalSymbol.SpeedLimit, (ushort)Lowered(own.Value, step));
            }

            return own;
        }

        private static int StepOf(Gantry g, int head)
        {
            if (g.LiveSteps.Count == 0)
            {
                return 0;
            }

            int lane = head;
            if (HeadRef.IsExtra(head))
            {
                int i = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head));
                lane = i >= 0 ? SignalRules.LinkedLane(g.Lanes, g.ExtraHeads[i]) : -1;
            }

            int step;
            if (lane >= 0 && lane < g.Lanes.Count)
            {
                return g.LiveSteps.TryGetValue(g.Lanes[lane].Segment, out step) ? step : 0;
            }

            // Not over a lane in particular: the busiest under it.
            step = 0;
            foreach (int s in g.LiveSteps.Values)
            {
                step = Mathf.Max(step, s);
            }

            return step;
        }

        /// <summary>True if a gantry has nothing live, so its signals are built as they always were.</summary>
        public static bool NothingLive(Gantry g)
        {
            return !g.LiveSpeed && g.Messages.Count == 0;
        }

        /// <summary>The change to make to a gantry's faces as its signals are built; null if it has nothing live.</summary>
        public static Func<int, bool, FaceSign, FaceSign> FilterFor(Gantry g)
        {
            if (!g.LiveSpeed && g.Messages.Count == 0)
            {
                return null;
            }

            float now = Time.realtimeSinceStartup;
            return (head, minus, own) => Show(g, head, minus, own, StepOf(g, head), now);
        }

        /// <summary>Looks in on the gantries that have anything live, redrawing only those whose signs have changed.</summary>
        public static void Tick(float now)
        {
            if (now < _next)
            {
                return;
            }

            _next = now + TickEvery;
            PageClock = now;
            bool traffic = now >= _nextTraffic;
            if (traffic)
            {
                _nextTraffic = now + TrafficEvery;
            }

            Gantries.Clear();
            GantryManager.GetAll(Gantries);
            foreach (Gantry g in Gantries)
            {
                lock (GantryManager.Lock)
                {
                    if (!g.LiveSpeed && g.Messages.Count == 0 && !HasChangingSign(g))
                    {
                        continue;
                    }

                    if (traffic)
                    {
                        ReadTraffic(g);
                    }

                    int stamp = Stamp(g, now);
                    if (stamp != g.LiveStamp)
                    {
                        g.LiveStamp = stamp;
                        g.SignalsDirty = true;
                    }
                }
            }
        }

        private static void ReadTraffic(Gantry g)
        {
            g.LiveSteps.Clear();
            g.LiveDensity = 0;
            NetManager net = Singleton<NetManager>.instance;
            int heavy = Mathf.Clamp(Settings.Current.HeavyTraffic, 5, 100);
            foreach (GantryLane lane in g.Lanes)
            {
                int density = net.m_segments.m_buffer[lane.Segment].m_trafficDensity;
                g.LiveSteps[lane.Segment] = StepFor(density, heavy);
                g.LiveDensity = Mathf.Max(g.LiveDensity, density);
            }
        }

        private static int Stamp(Gantry g, float now)
        {
            int stamp = 17;
            foreach (KeyValuePair<ushort, int> kv in g.LiveSteps)
            {
                stamp = stamp * 31 + kv.Key * 7 + kv.Value;
            }

            foreach (FaceMessages m in g.Messages)
            {
                stamp = stamp * 31 + (m.Ids.Count > 0 ? (int)(now / Mathf.Max(1, (int)m.Seconds)) % m.Ids.Count : 0);
            }

            foreach (GuideHead h in g.GuideSigns)
            {
                if (GantryTool.Guide.GuidePages.Changes(h.Design))
                {
                    stamp = stamp * 31 + GantryTool.Guide.GuidePages.Shown(h.Design, now, g.LiveDensity, IsEditing(g.Id, h.Id)).GetHashCode();
                }
            }

            return stamp;
        }

        public static byte[] Write(List<Gantry> gantries, object sync)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Version);
                lock (sync)
                {
                    var keep = gantries.FindAll(g => g.LiveSpeed || g.Messages.Count > 0);
                    w.Write(keep.Count);
                    foreach (Gantry g in keep)
                    {
                        w.Write(g.Id);
                        GantryData.WriteVector(w, g.Start);
                        GantryData.WriteVector(w, g.End);
                        w.Write(g.LiveSpeed);
                        w.Write(g.Messages.Count);
                        foreach (FaceMessages m in g.Messages)
                        {
                            w.Write(m.Head);
                            w.Write(m.Minus);
                            w.Write(m.Seconds);
                            w.Write(m.OnlyHeavy);
                            w.Write((byte)m.Ids.Count);
                            foreach (ushort id in m.Ids)
                            {
                                w.Write(id);
                            }
                        }
                    }
                }

                w.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Applies saved data to the matching gantries; never throws, and keeps what was read before an error.</summary>
        public static int Apply(byte[] data, List<Gantry> gantries, out string error)
        {
            error = null;
            if (data == null || data.Length == 0)
            {
                return 0;
            }

            int applied = 0;
            try
            {
                var byId = new Dictionary<int, Gantry>();
                foreach (Gantry g in gantries)
                {
                    byId[g.Id] = g;
                }

                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    byte version = r.ReadByte();
                    if (version != Version)
                    {
                        error = "unknown version " + version;
                        return 0;
                    }

                    int count = r.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int id = r.ReadInt32();
                        Vector3 start = GantryData.ReadVector(r);
                        Vector3 end = GantryData.ReadVector(r);
                        bool speed = r.ReadBoolean();
                        int messages = r.ReadInt32();
                        if (messages < 0 || messages > MaxMessages)
                        {
                            error = "message count " + messages + " out of range";
                            return applied;
                        }

                        Gantry g;
                        bool match = byId.TryGetValue(id, out g) && GantryData.Same(g.Start, start) && GantryData.Same(g.End, end);
                        if (match)
                        {
                            g.LiveSpeed = speed;
                            applied++;
                        }

                        for (int k = 0; k < messages; k++)
                        {
                            var m = new FaceMessages { Head = r.ReadInt32(), Minus = r.ReadBoolean(), Seconds = r.ReadByte(), OnlyHeavy = r.ReadBoolean() };
                            int n = r.ReadByte();
                            for (int q = 0; q < n; q++)
                            {
                                m.Ids.Add(r.ReadUInt16());
                            }

                            if (match)
                            {
                                g.Messages.Add(m);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                error = "cut short: " + e.Message;
            }

            return applied;
        }
    }
}
