using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Exceptions;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace RunHistoryReplay.RunHistoryReplayCode;

//You're recommended but not required to keep all your code in this package and all your assets in the RunHistoryReplay folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "RunHistoryReplay"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);

        harmony.PatchAll();
    }

    [HarmonyPatch]
    public static class RunHistoryReplayPatch
    {
        private static RunHistory? _currentRun;
        private static Hashtable? _charactersById = null;
        

        private static void LogAllChildren(Node parent)
        {
            Logger.Info("All child nodes of: " + parent.GetPath());
            foreach (Node child in parent.GetChildren())
            {
                Logger.Info("  " + child.GetName() + " (" + child.GetType().Name + ")");
            }
        }

        private static Player CreatePlayer()
        {
            if (_charactersById == null)
            {
                _charactersById = new Hashtable();
                foreach (CharacterModel character in ModelDb.AllCharacters)
                {
                    _charactersById.Add(character.Id, character);
                }
            }

            if (_currentRun == null)
            {
                Logger.Error("_currentRun is null");
                throw new SingletonInitException();
            }
            List<MapPointHistoryEntry> lastAct = _currentRun.MapPointHistory.Last();
            MapPointHistoryEntry previousRoom = lastAct[lastAct.Count - 2];
            PlayerMapPointHistoryEntry playerStats = previousRoom.PlayerStats.First();
            RunHistoryPlayer runHistoryPlayer = _currentRun.Players.First();
            CharacterModel runHistoryCharacter = (CharacterModel) _charactersById[runHistoryPlayer.Character];
            Player player = Player.CreateForNewRun(runHistoryCharacter, UnlockState.all, 1UL);
            SerializablePlayer serializablePlayer = player.ToSerializable();
            serializablePlayer.Relics = (List<SerializableRelic>) runHistoryPlayer.Relics;
            serializablePlayer.Deck = (List<SerializableCard>) runHistoryPlayer.Deck;
            serializablePlayer.CurrentHp = playerStats.CurrentHp;
            serializablePlayer.MaxHp = playerStats.MaxHp;
            player = Player.FromSerializable(serializablePlayer);
            return player;
        }

        private static SerializableRun createSerializableRun()
        {
            SerializableRun serializableRun = new SerializableRun();
            serializableRun.MapPointHistory = _currentRun.MapPointHistory;
            serializableRun.Ascension = _currentRun.Ascension;
            // serializableRun.Acts = _currentRun.Acts;
            // serializableRun.Players = _currentRun.Players;
            serializableRun.GameMode = _currentRun.GameMode;
            serializableRun.SerializableRng = new SerializableRunRngSet();
            serializableRun.SerializableRng.Seed = _currentRun.Seed;
            return serializableRun;
        }

        private static async Task StartFloorReplay(Player player, RunState runState, NGame game, ModelId roomModelId)
        {
            using (new NetLoadingHandle(RunManager.Instance.NetService))
            {
                Decimal hp = player.Creature.CurrentHp;
                await PreloadManager.LoadRunAssets(runState.Players.Select<Player, CharacterModel>((Func<Player, CharacterModel>) (p => p.Character)));
                await PreloadManager.LoadActAssets(runState.Acts[0]);
                RunManager.Instance.Launch();
                game.RootSceneContainer.SetCurrentScene((Control) NRun.Create(runState));
                await RunManager.Instance.EnterAct(0, false);
                // Reset health after Neow heals
                player.Creature.SetCurrentHpInternal(hp);
                game.AudioManager.StopMusic();
                new FightConsoleCmd().Process(player, [roomModelId.Entry]);
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(NRunHistory))]
        [HarmonyPatch("_Ready")]
        private static void AfterReady(NRunHistory __instance)
        {
            Logger.Info("Run history opened");
            NGame game = (NGame) __instance.GetTree().CurrentScene;
            LogAllChildren(__instance.GetTree().CurrentScene);
            Button replayFloorButton = new Button();
            replayFloorButton.SetText("Replay Floor");
            replayFloorButton.Pressed += () =>
            {
                Player player = CreatePlayer();
                RunState runState = RunState.CreateForTest([player], null, null, GameMode.Standard, _currentRun.Ascension);
                RunManager.Instance.SetUpNewSinglePlayer(runState, false);
                ModelId roomId = _currentRun.MapPointHistory.Last().Last().Rooms.Last().ModelId;
                StartFloorReplay(player, runState, game, roomId);
            };
            __instance.AddChild(replayFloorButton);
            NShareButton shareButton = __instance.GetNode<NShareButton>(new NodePath("ShareButton"));
            replayFloorButton.SetPosition(new Vector2(300, shareButton.GetPosition().Y));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NRunHistory))]
        [HarmonyPatch("DisplayRun")]
        private static void BeforeDisplayRun(RunHistory history)
        {
            _currentRun = history;
        }
    }
}