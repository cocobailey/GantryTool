using System;
using System.Collections.Generic;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// Owns all gantries of the loaded city. The list is guarded by <see cref="Lock"/> because
    /// it is also touched from the simulation thread (dirty marking) and the save thread.
    /// </summary>
    public static class GantryManager
    {
        /// <summary>Defaults for new gantries, from the options panel. "Post height" is the height above the road.</summary>
        public static float DefaultHeight => Settings.Current.HeightAboveRoad;
        public static float DefaultBeamDepth => Settings.Current.BeamDepth;
        public const float MinSpan = 6f;
        public const float MaxSpan = 80f;

        public static readonly object Lock = new object();

        private static readonly List<Gantry> _gantries = new List<Gantry>();
        private static int _nextId = 1;

        /// <summary>Copies the current gantries into <paramref name="output"/> (cleared first).</summary>
        public static void GetAll(List<Gantry> output)
        {
            output.Clear();
            lock (Lock)
            {
                output.AddRange(_gantries);
            }
        }

        public static int Count
        {
            get
            {
                lock (Lock)
                {
                    return _gantries.Count;
                }
            }
        }

        public static Gantry Add(Vector3 start, Vector3 end, float postHeight, float beamDepth)
        {
            var gantry = new Gantry
            {
                Start = start,
                End = end,
                PostHeight = postHeight,
                BeamDepth = beamDepth
            };

            lock (Lock)
            {
                gantry.Id = _nextId++;
                _gantries.Add(gantry);
            }

            return gantry;
        }

        /// <summary>Re-creates a gantry from a saved state under its original id (undo of a delete).</summary>
        public static Gantry Restore(GantryState state)
        {
            var gantry = new Gantry { Id = state.Id };
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == state.Id)
                    {
                        return _gantries[i];
                    }
                }

                state.ApplyTo(gantry);
                _gantries.Add(gantry);
                if (_nextId <= state.Id)
                {
                    _nextId = state.Id + 1;
                }
            }

            return gantry;
        }

        /// <summary>Moves a gantry's posts. Main thread; locked because the save thread reads positions.</summary>
        public static void SetEnds(Gantry g, Vector3 start, Vector3 end)
        {
            lock (Lock)
            {
                g.Start = start;
                g.End = end;
                g.MeshDirty = true;
            }
        }

        public static Gantry Find(int id)
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == id)
                    {
                        return _gantries[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Sets the manual symbols for lane <paramref name="laneIndex"/>. Auto on both faces removes the
        /// override. Main thread; the save thread reads overrides under the same lock.
        /// </summary>
        public static void SetOverride(Gantry g, int laneIndex, SignalSymbol minus, SignalSymbol plus)
        {
            if (laneIndex < 0 || laneIndex >= g.Lanes.Count)
            {
                return;
            }

            UndoStack.PushModified(g, "sign", coalesce: false);
            SetOverrideCore(g, laneIndex, minus, plus, true);
        }

        /// <summary>The manual symbols of a lane's two faces (Auto when none are set).</summary>
        public static void GetFaces(Gantry g, int laneIndex, out SignalSymbol minus, out SignalSymbol plus)
        {
            FaceSign m, p;
            GetFaces(g, laneIndex, out m, out p);
            minus = m.Symbol;
            plus = p.Symbol;
        }

        /// <summary>The manual signs (with values) of a lane's two faces (Auto when none are set).</summary>
        public static void GetFaces(Gantry g, int laneIndex, out FaceSign minus, out FaceSign plus)
        {
            minus = new FaceSign(SignalSymbol.Auto, 0);
            plus = new FaceSign(SignalSymbol.Auto, 0);
            lock (Lock)
            {
                if (HeadRef.IsExtra(laneIndex))
                {
                    int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(laneIndex));
                    if (e >= 0)
                    {
                        minus = g.ExtraHeads[e].MinusFace;
                        plus = g.ExtraHeads[e].PlusFace;
                    }

                    return;
                }

                int idx = SignalRules.FindOverride(g.Lanes, laneIndex, g.Overrides);
                if (idx >= 0)
                {
                    minus = g.Overrides[idx].MinusFace;
                    plus = g.Overrides[idx].PlusFace;
                }
            }
        }

        /// <summary>Sets one face of a lane, keeping the other face as it is.</summary>
        public static void SetFace(Gantry g, int laneIndex, bool minusFace, FaceSign sign)
        {
            if (HeadRef.IsExtra(laneIndex))
            {
                UndoStack.PushModified(g, "sign", coalesce: false);
                UpdateExtra(g, laneIndex, h =>
                {
                    if (minusFace)
                    {
                        h.Minus = sign.Symbol;
                        h.MinusValue = HasValue(sign.Symbol) ? sign.Value : (ushort)0;
                    }
                    else
                    {
                        h.Plus = sign.Symbol;
                        h.PlusValue = HasValue(sign.Symbol) ? sign.Value : (ushort)0;
                    }

                    return h;
                });
                return;
            }

            if (laneIndex < 0 || laneIndex >= g.Lanes.Count)
            {
                return;
            }

            FaceSign minus, plus;
            GetFaces(g, laneIndex, out minus, out plus);
            if (SignalRules.IsHidden(minus.Symbol, plus.Symbol))
            {
                // Picking a sign on a removed head brings it back with the other face on Auto.
                minus = plus = new FaceSign(SignalSymbol.Auto, 0);
            }

            UndoStack.PushModified(g, "sign", coalesce: false);
            SetFacesCore(g, laneIndex, minusFace ? sign : minus, minusFace ? plus : sign, true);
        }

        /// <summary>
        /// "Apply to": sets <paramref name="sign"/> on the same physical face of every lane, or on both faces of
        /// every lane, as one undo step. Lanes whose head was removed are left alone. Returns lanes changed.
        /// </summary>
        public static int ApplyToAllLanes(Gantry g, bool minusFace, FaceSign sign, bool bothFaces, bool sameDirectionOnly = false)
        {
            UndoStack.PushModified(g, "signs on all lanes", coalesce: false);
            int changed = 0;
            for (int i = 0; i < g.Lanes.Count; i++)
            {
                // Same direction: only lanes whose traffic sees this face (the minus face is seen by traffic heading
                // +side); two-way lanes are seen from both sides.
                int travel = g.Lanes[i].TravelSign;
                if (sameDirectionOnly && (minusFace ? travel < 0 : travel > 0))
                {
                    continue;
                }

                FaceSign minus, plus;
                GetFaces(g, i, out minus, out plus);
                if (SignalRules.IsHidden(minus.Symbol, plus.Symbol))
                {
                    continue;
                }

                if (bothFaces || minusFace)
                {
                    minus = sign;
                }

                if (bothFaces || !minusFace)
                {
                    plus = sign;
                }

                SetFacesCore(g, i, minus, plus, false);
                changed++;
            }

            // Extra heads linked to a lane follow their lane; unlinked ones are one-off signs and stay as they are.
            lock (Lock)
            {
                for (int k = 0; k < g.ExtraHeads.Count; k++)
                {
                    ExtraHead h = g.ExtraHeads[k];
                    int lane = SignalRules.LinkedLane(g.Lanes, h);
                    if (lane < 0)
                    {
                        continue;
                    }

                    int travel = g.Lanes[lane].TravelSign;
                    if (sameDirectionOnly && (minusFace ? travel < 0 : travel > 0))
                    {
                        continue;
                    }

                    ushort value = HasValue(sign.Symbol) ? sign.Value : (ushort)0;
                    if (bothFaces || minusFace)
                    {
                        h.Minus = sign.Symbol;
                        h.MinusValue = value;
                    }

                    if (bothFaces || !minusFace)
                    {
                        h.Plus = sign.Symbol;
                        h.PlusValue = value;
                    }

                    g.ExtraHeads[k] = h;
                    changed++;
                }

                g.SignalsDirty = true;
            }

            UndoStack.DiscardIfUnchanged(g);
            Log.Info("Applied " + sign.Symbol + (sign.Value != 0 ? " " + sign.Value : "") + " to " +
                     (bothFaces ? "both faces" : "one face") + " of " + changed + (sameDirectionOnly ? " same-direction" : "") +
                     " lanes on gantry #" + g.Id);
            return changed;
        }

        private static void SetOverrideCore(Gantry g, int laneIndex, SignalSymbol minus, SignalSymbol plus, bool log)
        {
            SetFacesCore(g, laneIndex, new FaceSign(minus, 0), new FaceSign(plus, 0), log);
        }

        private static void SetFacesCore(Gantry g, int laneIndex, FaceSign minus, FaceSign plus, bool log)
        {
            float offset = UpdateEntry(g, laneIndex, o =>
            {
                o.Minus = minus.Symbol;
                o.MinusValue = HasValue(minus.Symbol) ? minus.Value : (ushort)0;
                o.Plus = plus.Symbol;
                o.PlusValue = HasValue(plus.Symbol) ? plus.Value : (ushort)0;
                return o;
            });

            if (log)
            {
                Log.Info("Override gantry #" + g.Id + " lane @" + offset.ToString("F2") + "m: -side face=" + Describe(minus) +
                         ", +side face=" + Describe(plus) + " (" + g.Overrides.Count + " overrides on this gantry)");
            }
        }

        /// <summary>Signs that carry a value: speed limits (km/h), custom signs (library id), ramp signs (meter node).</summary>
        private static bool HasValue(SignalSymbol s)
        {
            return s == SignalSymbol.SpeedLimit || s == SignalSymbol.Custom || s == SignalSymbol.RampMetered;
        }

        private static string Describe(FaceSign f)
        {
            if (f.Symbol == SignalSymbol.RampMetered)
            {
                return "RampMetered (node " + f.Value + ")";
            }

            return f.Symbol == SignalSymbol.SpeedLimit ? "Speed " + f.Value + " km/h" : f.Symbol.ToString();
        }

        /// <summary>
        /// Sets a head's position along the beam (metres from its lane centre) and box size (0 = the gantry's size),
        /// keeping its signs. <paramref name="undoKey"/> null = no undo step (the caller records one, e.g. a drag).
        /// </summary>
        public static void SetHeadLayout(Gantry g, int laneIndex, float nudge, float size, string undoKey)
        {
            if (HeadRef.IsExtra(laneIndex))
            {
                // Extra heads: "nudge" is the offset from the linked lane, or the position along the beam when unlinked.
                if (undoKey != null)
                {
                    UndoStack.PushModified(g, undoKey);
                }

                UpdateExtra(g, laneIndex, h =>
                {
                    if (h.Linked)
                    {
                        h.Nudge = Mathf.Abs(nudge) < 0.005f ? 0f : nudge;
                    }
                    else
                    {
                        h.Along = Mathf.Clamp(nudge, 0f, Mathf.Max(0f, g.Span));
                    }

                    h.Size = size;
                    return h;
                });
                return;
            }

            if (laneIndex < 0 || laneIndex >= g.Lanes.Count)
            {
                return;
            }

            if (undoKey != null)
            {
                UndoStack.PushModified(g, undoKey);
            }

            UpdateEntry(g, laneIndex, o =>
            {
                o.Nudge = Mathf.Abs(nudge) < 0.005f ? 0f : nudge;
                o.Size = size;
                return o;
            });
        }

        /// <summary>
        /// Creates or updates the override entry of a lane (keeping whatever <paramref name="change"/> doesn't touch),
        /// re-keys it to the lane's current position and direction, and drops it once it carries nothing.
        /// Returns the lane offset.
        /// </summary>
        private static float UpdateEntry(Gantry g, int laneIndex, Func<SignalOverride, SignalOverride> change)
        {
            float offset = g.Lanes[laneIndex].Offset;
            lock (Lock)
            {
                int idx = SignalRules.FindOverride(g.Lanes, laneIndex, g.Overrides);
                SignalOverride o = idx >= 0 ? g.Overrides[idx] : new SignalOverride();
                o = change(o);
                o.Offset = offset;
                o.Sign = (sbyte)g.Lanes[laneIndex].TravelSign;

                if (o.IsEmpty)
                {
                    if (idx >= 0)
                    {
                        g.Overrides.RemoveAt(idx);
                    }
                }
                else if (idx >= 0)
                {
                    g.Overrides[idx] = o;
                }
                else
                {
                    g.Overrides.Add(o);
                }

                g.SignalsDirty = true;
            }

            return offset;
        }

        /// <summary>A head's current layout: nudge along the beam and size override (0 = gantry size).</summary>
        public static void GetHeadLayout(Gantry g, int laneIndex, out float nudge, out float size)
        {
            nudge = 0f;
            size = 0f;
            lock (Lock)
            {
                if (HeadRef.IsExtra(laneIndex))
                {
                    int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(laneIndex));
                    if (e >= 0)
                    {
                        nudge = g.ExtraHeads[e].Linked ? g.ExtraHeads[e].Nudge : g.ExtraHeads[e].Along;
                        size = g.ExtraHeads[e].Size;
                    }

                    return;
                }

                int idx = SignalRules.FindOverride(g.Lanes, laneIndex, g.Overrides);
                if (idx >= 0)
                {
                    nudge = g.Overrides[idx].Nudge;
                    size = g.Overrides[idx].Size;
                }
            }
        }

        /// <summary>
        /// What a head's layout "nudge" is measured from along the beam: its lane's centre, or the start post for an
        /// unlinked extra head (whose nudge is its position).
        /// </summary>
        public static float HeadBase(Gantry g, int head)
        {
            lock (Lock)
            {
                if (!HeadRef.IsExtra(head))
                {
                    return head >= 0 && head < g.Lanes.Count ? g.Lanes[head].Offset : 0f;
                }

                int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head));
                int lane = e >= 0 ? SignalRules.LinkedLane(g.Lanes, g.ExtraHeads[e]) : -1;
                return lane >= 0 ? g.Lanes[lane].Offset : 0f;
            }
        }

        // ------------------------------------------------------------------------------ extra heads

        /// <summary>Whether head <paramref name="head"/> (<see cref="HeadRef"/> numbering) exists on the gantry.</summary>
        public static bool HeadExists(Gantry g, int head)
        {
            if (g == null || head < 0)
            {
                return false;
            }

            lock (Lock)
            {
                return HeadRef.IsExtra(head) ? HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head)) >= 0 : head < g.Lanes.Count;
            }
        }

        /// <summary>Whether the gantry can take another extra head.</summary>
        public static bool CanAddExtraHead(Gantry g)
        {
            lock (Lock)
            {
                return g.ExtraHeads.Count < HeadRef.MaxExtraHeads;
            }
        }

        /// <summary>
        /// Adds an unlinked head with blank faces at <paramref name="along"/> (clamped to the beam). Returns its head
        /// number, or -1 when the gantry already has the most extra heads allowed.
        /// </summary>
        public static int AddExtraHead(Gantry g, float along)
        {
            int head;
            UndoStack.PushModified(g, "add sign", coalesce: false);
            lock (Lock)
            {
                if (g.ExtraHeads.Count >= HeadRef.MaxExtraHeads)
                {
                    return -1;
                }

                byte id = 1;
                foreach (ExtraHead e in g.ExtraHeads)
                {
                    if (e.Id >= id)
                    {
                        id = (byte)(e.Id + 1);
                    }
                }

                g.ExtraHeads.Add(new ExtraHead
                {
                    Id = id,
                    Along = Mathf.Clamp(along, 0f, Mathf.Max(0f, g.Span)),
                    Minus = SignalSymbol.Blank,
                    Plus = SignalSymbol.Blank
                });
                g.SignalsDirty = true;
                head = HeadRef.Of(id);
            }

            Log.Info("Added sign " + head + " to gantry #" + g.Id);
            return head;
        }

        /// <summary>Sets both faces of an extra head as one undo step (Reset signs).</summary>
        public static void SetExtraFaces(Gantry g, int head, FaceSign minus, FaceSign plus)
        {
            UndoStack.PushModified(g, "sign", coalesce: false);
            UpdateExtra(g, head, h =>
            {
                h.Minus = minus.Symbol;
                h.MinusValue = HasValue(minus.Symbol) ? minus.Value : (ushort)0;
                h.Plus = plus.Symbol;
                h.PlusValue = HasValue(plus.Symbol) ? plus.Value : (ushort)0;
                return h;
            });
            UndoStack.DiscardIfUnchanged(g);
        }

        /// <summary>Where a head is along the beam right now (metres from the start post).</summary>
        public static float HeadAlong(Gantry g, int head)
        {
            float along, size;
            lock (Lock)
            {
                Render.SignalMeshBuilder.HeadLayout(g.Lanes, g.Overrides, g.ExtraHeads, head, g.SignalSize, g.BeamDepth, g.Span,
                    out along, out size);
            }

            return along;
        }

        /// <summary>Deletes an extra head (undoable).</summary>
        public static void RemoveExtraHead(Gantry g, int head)
        {
            UndoStack.PushModified(g, "delete sign", coalesce: false);
            lock (Lock)
            {
                int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head));
                if (e >= 0)
                {
                    g.ExtraHeads.RemoveAt(e);
                    g.SignalsDirty = true;
                }
            }

            UndoStack.DiscardIfUnchanged(g);
        }

        /// <summary>
        /// Links an extra head to lane <paramref name="laneIndex"/> (or unlinks it with -1) without moving it. Unlinking
        /// turns Auto and Road speed faces blank, since there is no lane left to read.
        /// </summary>
        public static void LinkExtraHead(Gantry g, int head, int laneIndex)
        {
            float along, size;
            lock (Lock)
            {
                int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head));
                if (e < 0)
                {
                    return;
                }

                Render.SignalMeshBuilder.ExtraLayout(g.Lanes, g.ExtraHeads[e], g.SignalSize, g.BeamDepth, g.Span, out along, out size);
            }

            UndoStack.PushModified(g, "link sign", coalesce: false);
            UpdateExtra(g, head, h =>
            {
                if (laneIndex >= 0 && laneIndex < g.Lanes.Count)
                {
                    h.Linked = true;
                    h.LinkOffset = g.Lanes[laneIndex].Offset;
                    h.LinkSign = (sbyte)g.Lanes[laneIndex].TravelSign;
                    h.Nudge = along - g.Lanes[laneIndex].Offset;
                }
                else
                {
                    h = Unlinked(h, along);
                }

                return h;
            });
            UndoStack.DiscardIfUnchanged(g);
        }

        /// <summary>
        /// Keeps linked extra heads on their lanes after a rescan: re-keys each to its lane's current position, and
        /// unlinks (in place) any whose lane is gone. Main thread, after the lanes were rebuilt.
        /// </summary>
        public static void RefreshExtraLinks(Gantry g)
        {
            lock (Lock)
            {
                for (int k = 0; k < g.ExtraHeads.Count; k++)
                {
                    ExtraHead h = g.ExtraHeads[k];
                    if (!h.Linked)
                    {
                        continue;
                    }

                    int lane = SignalRules.LinkedLane(g.Lanes, h);
                    if (lane >= 0)
                    {
                        h.LinkOffset = g.Lanes[lane].Offset;
                        h.LinkSign = (sbyte)g.Lanes[lane].TravelSign;
                        h.Along = Mathf.Clamp(h.LinkOffset + h.Nudge, 0f, Mathf.Max(0f, g.Span));
                    }
                    else
                    {
                        h = Unlinked(h, h.Along);
                        Log.Info("Gantry #" + g.Id + ": sign " + HeadRef.Of(h.Id) + " lost its lane; now unlinked");
                    }

                    g.ExtraHeads[k] = h;
                }
            }
        }

        private static ExtraHead Unlinked(ExtraHead h, float along)
        {
            h.Linked = false;
            h.Along = along;
            h.Nudge = 0f;
            if (h.Minus == SignalSymbol.Auto || h.Minus == SignalSymbol.SpeedAuto)
            {
                h.Minus = SignalSymbol.Blank;
                h.MinusValue = 0;
            }

            if (h.Plus == SignalSymbol.Auto || h.Plus == SignalSymbol.SpeedAuto)
            {
                h.Plus = SignalSymbol.Blank;
                h.PlusValue = 0;
            }

            return h;
        }

        /// <summary>Changes one extra head under the lock and schedules a signal rebuild.</summary>
        private static void UpdateExtra(Gantry g, int head, Func<ExtraHead, ExtraHead> change)
        {
            lock (Lock)
            {
                int e = HeadRef.IndexOf(g.ExtraHeads, HeadRef.ExtraId(head));
                if (e < 0)
                {
                    return;
                }

                g.ExtraHeads[e] = change(g.ExtraHeads[e]);
                g.SignalsDirty = true;
            }
        }

        /// <summary>How many faces, on all gantries, show custom sign <paramref name="id"/>.</summary>
        public static int CountCustomUses(ushort id)
        {
            int n = 0;
            lock (Lock)
            {
                foreach (Gantry g in _gantries)
                {
                    foreach (SignalOverride o in g.Overrides)
                    {
                        n += (o.Minus == SignalSymbol.Custom && o.MinusValue == id ? 1 : 0) +
                             (o.Plus == SignalSymbol.Custom && o.PlusValue == id ? 1 : 0);
                    }

                    foreach (ExtraHead h in g.ExtraHeads)
                    {
                        n += (h.Minus == SignalSymbol.Custom && h.MinusValue == id ? 1 : 0) +
                             (h.Plus == SignalSymbol.Custom && h.PlusValue == id ? 1 : 0);
                    }
                }
            }

            return n;
        }

        /// <summary>Deletes a custom sign: every face showing it goes back to Auto. Returns how many faces changed.</summary>
        public static int DeleteCustomSign(ushort id)
        {
            int changed = 0;
            lock (Lock)
            {
                foreach (Gantry g in _gantries)
                {
                    bool touched = false;
                    for (int k = 0; k < g.Overrides.Count; k++)
                    {
                        SignalOverride o = g.Overrides[k];
                        if (o.Minus == SignalSymbol.Custom && o.MinusValue == id)
                        {
                            o.Minus = SignalSymbol.Auto;
                            o.MinusValue = 0;
                            changed++;
                            touched = true;
                        }

                        if (o.Plus == SignalSymbol.Custom && o.PlusValue == id)
                        {
                            o.Plus = SignalSymbol.Auto;
                            o.PlusValue = 0;
                            changed++;
                            touched = true;
                        }

                        g.Overrides[k] = o;
                    }

                    // Extra heads have no Auto without a lane, so their faces go blank instead.
                    for (int k = 0; k < g.ExtraHeads.Count; k++)
                    {
                        ExtraHead h = g.ExtraHeads[k];
                        if (h.Minus == SignalSymbol.Custom && h.MinusValue == id)
                        {
                            h.Minus = h.Linked ? SignalSymbol.Auto : SignalSymbol.Blank;
                            h.MinusValue = 0;
                            changed++;
                            touched = true;
                        }

                        if (h.Plus == SignalSymbol.Custom && h.PlusValue == id)
                        {
                            h.Plus = h.Linked ? SignalSymbol.Auto : SignalSymbol.Blank;
                            h.PlusValue = 0;
                            changed++;
                            touched = true;
                        }

                        g.ExtraHeads[k] = h;
                    }

                    if (touched)
                    {
                        g.SignalsDirty = true;
                    }
                }
            }

            CustomSigns.Remove(id);
            Log.Info("Deleted custom sign #" + id + "; " + changed + " faces back to Auto");
            return changed;
        }

        /// <summary>Schedules every gantry's signal mesh for a rebuild (sign textures changed). Main thread.</summary>
        public static void MarkAllSignalsDirty()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    _gantries[i].SignalsDirty = true;
                }
            }
        }

        /// <summary>Schedules every gantry for a rescan + rebuild (option changes). Main thread.</summary>
        public static void MarkAllDirty()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    _gantries[i].MeshDirty = true;
                }
            }
        }

        /// <summary>Applies the current default post height / beam depth to every existing gantry.</summary>
        public static int ApplyDefaultsToAll()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    _gantries[i].HeightAboveRoad = DefaultHeight;
                    _gantries[i].BeamDepth = DefaultBeamDepth;
                    _gantries[i].MeshDirty = true;
                }

                return _gantries.Count;
            }
        }

        /// <summary>Removes a gantry. Main thread only (destroys its mesh).</summary>
        public static bool Remove(int id)
        {
            Gantry removed = null;
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    if (_gantries[i].Id == id)
                    {
                        removed = _gantries[i];
                        _gantries.RemoveAt(i);
                        break;
                    }
                }
            }

            if (removed == null)
            {
                return false;
            }

            DestroyMesh(removed);
            return true;
        }

        /// <summary>
        /// Replaces the whole set with gantries read from a save; meshes are built lazily by the renderer.
        /// Runs on the loading/simulation thread, so it must not call UnityEngine.Object.Destroy. The set is
        /// normally already empty (cleared on the main thread in OnLevelUnloading).
        /// </summary>
        public static void ReplaceAll(List<Gantry> loaded)
        {
            lock (Lock)
            {
                if (_gantries.Count > 0)
                {
                    Log.Warn("ReplaceAll: dropping " + _gantries.Count + " gantries still in memory");
                    _gantries.Clear();
                }

                int maxId = 0;
                for (int i = 0; i < loaded.Count; i++)
                {
                    loaded[i].MeshDirty = true;
                    _gantries.Add(loaded[i]);
                    maxId = Math.Max(maxId, loaded[i].Id);
                }

                _nextId = maxId + 1;
            }
        }

        /// <summary>Destroys meshes and forgets every gantry. Called on level unload.</summary>
        public static void Clear()
        {
            lock (Lock)
            {
                for (int i = 0; i < _gantries.Count; i++)
                {
                    DestroyMesh(_gantries[i]);
                }

                _gantries.Clear();
                _nextId = 1;
            }
        }

        public static void DestroyMesh(Gantry gantry)
        {
            try
            {
                if (gantry.Mesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.Mesh);
                    gantry.Mesh = null;
                }

                if (gantry.SignalMesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.SignalMesh);
                    gantry.SignalMesh = null;
                }

                if (gantry.BlinkMesh != null)
                {
                    UnityEngine.Object.Destroy(gantry.BlinkMesh);
                    gantry.BlinkMesh = null;
                }

                for (int i = 0; i < gantry.RampBeacons.Count; i++)
                {
                    UnityEngine.Object.Destroy(gantry.RampBeacons[i].Mesh);
                }

                gantry.RampBeacons.Clear();
            }
            catch (Exception e)
            {
                Log.Error("DestroyMesh failed for gantry #" + gantry.Id, e);
            }
        }
    }
}
