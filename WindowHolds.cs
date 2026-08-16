// The three pieces of global game state the settings window borrows, and gives back.
//
// Green Hell keeps the mouse locked to the camera, so an IMGUI window full of tabs and sliders is
// unusable until something explicitly releases the cursor. The window shipped without this and he
// had to open the GAME's own settings just to get a pointer back before he could click anything.
//
// Copied deliberately from Pickup Doctor, which has been doing this correctly for months, rather
// than reinvented - including the part that matters most, which is the giving back.
//
// WHY RELEASE IS ITS OWN CLASS
// These holds outlive the window. If the update loop faults while the window is open, the window
// stops drawing but the game stays PAUSED with a free cursor and no key that helps - an
// unrecoverable freeze short of restarting the game. So every exit path routes through
// ReleaseAll(): the normal close, the fault guard, and OnDestroy. Taking a hold is easy and
// forgetting to return one is the bug.
//
// Each hold is LATCHED. The game reference-counts BlockRotation/BlockMoves and Pause, so they nest
// safely with anything else that blocks input - but only while we stay symmetric, and the latch is
// what guarantees that. Blocking twice and releasing once leaves the player permanently unable to
// move, in a way that survives closing the window.
//
// Input is blocked even when pause is OFF: the cursor is free either way, so the mouse must stop
// steering the character regardless.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using BepInEx.Logging;
using UnityEngine;

namespace FieldNotes
{
    internal class WindowHolds
    {
        private readonly ManualLogSource _log;
        private bool _cursorFree;
        private bool _inputBlocked;
        private bool _paused;

        internal WindowHolds(ManualLogSource log) { _log = log; }

        internal bool AnythingHeld { get { return _cursorFree || _inputBlocked || _paused; } }

        internal void Take(bool pause)
        {
            CursorFree(true);
            InputBlocked(true);
            if (pause) Paused(true);
            else Paused(false);
        }

        /// <summary>Hand back everything, in the order that leaves the game usable if one step throws.</summary>
        internal void ReleaseAll()
        {
            try { Paused(false); } catch (Exception ex) { Warn("unpause failed: " + ex.Message); }
            try { InputBlocked(false); } catch (Exception ex) { Warn("input restore failed: " + ex.Message); }
            try { CursorFree(false); } catch (Exception ex) { Warn("cursor restore failed: " + ex.Message); }
        }

        private void CursorFree(bool free)
        {
            if (free == _cursorFree) return;
            try
            {
                CursorManager cm = CursorManager.Get();
                if (cm == null) return;
                if (free)
                {
                    cm.SetCursorLockState(CursorLockMode.None);
                    cm.ShowCursor(true, false);
                }
                else
                {
                    cm.ShowCursor(false, false);
                    cm.SetCursorLockState(CursorLockMode.Locked);
                }
                _cursorFree = free;
            }
            catch (Exception ex) { Warn("cursor state change failed: " + ex.Message); }
        }

        private void InputBlocked(bool block)
        {
            if (block == _inputBlocked) return;
            try
            {
                Player p = Player.Get();
                if (p == null) return;
                if (block) { p.BlockRotation(); p.BlockMoves(); }
                else { p.UnblockRotation(); p.UnblockMoves(); }
                _inputBlocked = block;
            }
            catch (Exception ex) { Warn("could not " + (block ? "block" : "unblock") + " input: " + ex.Message); }
        }

        private void Paused(bool pause)
        {
            if (pause == _paused) return;
            try
            {
                MainLevel lvl = MainLevel.Instance;
                if (lvl == null) return;
                lvl.Pause(pause);
                _paused = pause;
            }
            catch (Exception ex) { Warn("could not " + (pause ? "pause" : "unpause") + ": " + ex.Message); }
        }

        private void Warn(string msg)
        {
            if (_log != null) _log.LogWarning(msg);
        }
    }
}
