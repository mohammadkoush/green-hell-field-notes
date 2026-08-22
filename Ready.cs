// Field Notes - do not draw the minimap until the game is actually playable.
//
// HIS ASK: "The minimap launches before my game is available for me to play it... I would like a
// delay for displaying the minimap. Till the game is fully loaded."
//
// HE ASKED FOR A DELAY AND THIS IS NOT ONE, DELIBERATELY, and he agreed when it was put to him:
// "Wait for the signal (recommended)." A timer is a guess - one tuned on his machine is wrong on a
// slower one, wrong again after a patch changes load times, and wrong every time he adds a mod. It
// also cannot tell "still loading" from "loaded, sitting in the main menu". The game already
// publishes the answer, so this asks the question he actually means: IS THE GAME READY.
//
// ---------------------------------------------------------------------------------------------
// WHAT THE GAME ACTUALLY PUBLISHES
//
// Read out of Assembly-CSharp with Cecil before a line of this was written, because I have been
// caught twice this week trusting what a field is CALLED instead of checking what it is. Every one
// of these turned out to be PUBLIC, so none of it needs reflection - the compiler checks the lot,
// and a game update that removes any of them breaks the build instead of failing silently at
// runtime. That is worth more than the flexibility reflection would have bought.
//
//   GreenHellGame.m_LoadGameState        enum, and the decisive one. Runs None -> PrepareScenes ->
//                                        LoadingScenes -> PreloadScheduled -> PreloadCompleted ->
//                                        FullLoadScheduled -> FullLoadWaitingForScenario ->
//                                        FullLoadCompleted. Only the last means loaded.
//   GreenHellGame.m_InitialSequenceComplete   bool, the startup sequence is done
//   MainLevel.Instance                   null until there is a level at all - the main menu has none
//   MainLevel.m_LevelStarted             bool property, public getter
//   MainLevel.AreStreamersLoaded()       the terrain around him has actually streamed in
//   MainLevel.IsAnyStreamerLoading()     ...and none is still coming
//   LoadingScreen.Get().m_Active         the loading screen is still up - must be FALSE
//   Player.Get()                         he exists
//
// ALL OF THEM MUST AGREE, because each is true too early on its own. The streamer pair is the
// reason a minimap drawn "after loading" could still be a map of nothing: the level can have
// started while the ground around him has not arrived.
//
// THE SETTLE IS THE ONLY GUESS LEFT, and it is bounded by real signals rather than standing in for
// them - a short pause after everything agrees, because control can arrive a moment after the game
// says it is done. Default small; the setting is there if it is not enough.
//
// Language level is C# 5 (stock Framework csc.exe) - no ?., no $"", no ??=.

using System;
using BepInEx.Configuration;
using UnityEngine;

namespace FieldNotes
{
    public partial class FieldNotesPlugin
    {
        private ConfigEntry<bool>  _waitForReady;
        private ConfigEntry<float> _readySettle;

        private static float s_AgreedAt = -1f;      // when every signal first agreed
        private static bool  s_ReadyReported;
        private static int   s_ReadyFrame = -1;     // OnGUI runs twice a frame; answer once
        private static bool  s_ReadyCached;

        // Handles for the settings window, kept here beside what they control rather than in the
        // main block, so this whole feature is one file to read or to delete.
        internal ConfigEntry<bool>  CfgWaitForReady { get { return _waitForReady; } }
        internal ConfigEntry<float> CfgReadySettle  { get { return _readySettle; } }

        private void BindReadyConfig()
        {
            _waitForReady = Config.Bind("Minimap", "WaitUntilTheGameIsPlayable", true,
                "Keep the minimap off screen until the game has genuinely finished loading, instead " +
                "of drawing it over the loading screen and the main menu. This waits for the game's " +
                "own all-clear - including the terrain around you having streamed in - rather than " +
                "counting off a fixed number of seconds.");

            _readySettle = Config.Bind("Minimap", "SettleSeconds", 1f,
                new ConfigDescription(
                    "A short pause after the game reports itself ready, because control of your " +
                    "character can arrive a moment after the loading does.",
                    new AcceptableValueRange<float>(0f, 15f)));
        }

        /// <summary>
        /// Is the game genuinely playable? Every clause must pass; anything missing counts as NOT
        /// ready. Waiting a moment too long costs him a second of looking at his own game - drawing
        /// too early is the thing he asked me to fix, twice.
        /// </summary>
        internal bool GameIsPlayable()
        {
            // OnGUI is called at least twice per frame (Layout, then Repaint). AreStreamersLoaded
            // walks a list, so the verdict is worked out once a frame and handed out after that.
            if (s_ReadyFrame == Time.frameCount) return s_ReadyCached;
            s_ReadyFrame = Time.frameCount;
            s_ReadyCached = Decide();
            return s_ReadyCached;
        }

        private bool Decide()
        {
            try
            {
                if (_waitForReady == null || !_waitForReady.Value) return true;

                GreenHellGame game = GreenHellGame.Instance;
                if (game == null) return NotYet();

                // The decisive one. Every earlier value in this enum means loading of some kind.
                if (game.m_LoadGameState != LoadGameState.FullLoadCompleted) return NotYet();
                if (!game.m_InitialSequenceComplete) return NotYet();

                // The loading screen has its own flag and it is not always down when the load
                // reports finished - it fades. Asked through Get() because m_LoadingScreen on the
                // game object is set up later than the screen itself exists.
                LoadingScreen screen = LoadingScreen.Get();
                if (screen != null && screen.m_Active) return NotYet();

                MainLevel level = MainLevel.Instance;
                if (level == null) return NotYet();          // main menu has no level
                if (!level.m_LevelStarted) return NotYet();

                // The ground he is standing on. A minimap drawn before the streamers finish is a
                // map of nothing, which looks exactly like the bug he reported.
                if (!level.AreStreamersLoaded()) return NotYet();
                if (level.IsAnyStreamerLoading()) return NotYet();

                if (Player.Get() == null) return NotYet();

                // The settle starts when everything first agrees, and NotYet() clears it - so
                // loading a second save waits again rather than inheriting the last countdown.
                if (s_AgreedAt < 0f) s_AgreedAt = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - s_AgreedAt < _readySettle.Value) return false;

                if (!s_ReadyReported)
                {
                    s_ReadyReported = true;
                    Logger.LogInfo("minimap held back until the game was playable: load state "
                        + game.m_LoadGameState + ", level started, terrain streamed in, loading "
                        + "screen down, then a " + _readySettle.Value + "s settle. If it still "
                        + "appears too early or too late, SettleSeconds is the dial.");
                }
                return true;
            }
            catch (Exception)
            {
                // A fault in the gate must never take his minimap away permanently.
                return true;
            }
        }

        private static bool NotYet()
        {
            s_AgreedAt = -1f;
            return false;
        }
    }
}
