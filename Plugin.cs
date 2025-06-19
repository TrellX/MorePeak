using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Realtime;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace MorePeakPlugin
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
		public static int MAX_PLAYERS;

		private static ConfigEntry<int> configMaxPlayers;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

			configMaxPlayers = ((BaseUnityPlugin)this).Config.Bind<int>("General", "MaxPlayers", 8, "Max number of players for the lobby, really high numbers untested");
			MAX_PLAYERS = configMaxPlayers.Value;

			if (MAX_PLAYERS == 0)
			{
				MAX_PLAYERS = 8;
			}
			else if (MAX_PLAYERS > 100)
			{
				MAX_PLAYERS = 100;
			}

			Harmony.CreateAndPatchAll(typeof(Patch));
        }
    }

	public class Patch 
	{
		private static AudioMixerGroup[] amg = new AudioMixerGroup[LobbyHelper.GetLobbyMaxConfig() - 4];

		// lobby increase
		[HarmonyPatch(typeof(NetworkConnector), "HostRoomOptions")]
		[HarmonyPostfix]
		public static void HostRoomOptionsPostfix(ref RoomOptions __result)
		{
			__result.MaxPlayers = LobbyHelper.GetLobbyMaxConfig();
		}

		[HarmonyPatch(typeof(MainMenuMainPage))]
		[HarmonyPriority(800)]
		[HarmonyTranspiler]
		[HarmonyPatch( "PlayClicked")]
		private static IEnumerable<CodeInstruction> PlayClickedTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions, (ILGenerator)null)
			.SearchForward((Func<CodeInstruction, bool>)((CodeInstruction instruction) => instruction.opcode == OpCodes.Ldsfld))
			.ThrowIfInvalid("Could not find max players constant")
			.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(LobbyHelper), "GetLobbyMaxConfig", (Type[])null, (Type[])null)))
				.InstructionEnumeration();
		}

		// audio fixes
		[HarmonyPatch(typeof(AudioLevels), "OnEnable")]
		[HarmonyPrefix]
		public static bool OnEnablePrefix(AudioLevels __instance)
		{
			if (__instance.sliders.Count < LobbyHelper.GetLobbyMaxConfig()) {
				GameObject slider = __instance.sliders[0].gameObject;
				for (int i = __instance.sliders.Count; i < LobbyHelper.GetLobbyMaxConfig(); i++) {
					GameObject sliderNew = GameObject.Instantiate(slider, __instance.transform);
					__instance.sliders.Add(sliderNew.GetComponent<AudioLevelSlider>());
				}
			}
			return true;
		}

		[HarmonyPatch(typeof(PlayerHandler))]
		[HarmonyPriority(800)]
		[HarmonyTranspiler]
		[HarmonyPatch( "AssignMixerGroup")]
		private static IEnumerable<CodeInstruction> AssignMixerGroupTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			return new CodeMatcher(instructions, (ILGenerator)null)
			.SearchForward((Func<CodeInstruction, bool>)((CodeInstruction instruction) => instruction.opcode == OpCodes.Ldc_I4_4))
			.ThrowIfInvalid("Could not find max players constant")
			.SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(LobbyHelper), "GetLobbyMaxConfig", (Type[])null, (Type[])null)))
				.InstructionEnumeration();
		}

		[HarmonyPatch(typeof(CharacterVoiceHandler), "GetMixerGroup")]
		[HarmonyPrefix]
		public static bool GetMixerGroupPrefix(CharacterVoiceHandler __instance, ref AudioMixerGroup __result, byte group)
		{
			if (Patch.amg[0] == null) 
			{
				for (int i = 0; i < Patch.amg.Length; i++)
				{
					Patch.amg[i] = AudioMixerGroup.Instantiate(Traverse.Create(__instance).Field("m_mixerGroup1").GetValue() as AudioMixerGroup);
					Patch.amg[i].name = "Voice" + (i + 5);
				}
			}

			AudioMixerGroup result;
			switch (group)
			{
			case 0:
				result = Traverse.Create(__instance).Field("m_mixerGroup1").GetValue() as AudioMixerGroup;
				break;
			case 1:
				result = Traverse.Create(__instance).Field("m_mixerGroup2").GetValue() as AudioMixerGroup;
				break;
			case 2:
				result = Traverse.Create(__instance).Field("m_mixerGroup3").GetValue() as AudioMixerGroup;
				break;
			case 3:
				result = Traverse.Create(__instance).Field("m_mixerGroup4").GetValue() as AudioMixerGroup;
				break;
			default:
				result = Traverse.Create(__instance).Field("m_mixerGroup1").GetValue() as AudioMixerGroup;
				break;
			}

			if (group >= 4) {
				result = Patch.amg[group-4];
			}

			__result = result;
			return false;
		}

		/// Endgame stats stuff

		[HarmonyPatch(typeof(WaitingForPlayersUI), "Update")]
		[HarmonyPrefix]
		public static bool UpdatePrefix(WaitingForPlayersUI __instance)
		{
			if (__instance.scoutImages.Length <= 4) 
			{
				GameObject readyScout = __instance.scoutImages[0].gameObject;
				Array.Resize(ref __instance.scoutImages, LobbyHelper.GetLobbyMaxConfig());
				for (int i = 4; i < LobbyHelper.GetLobbyMaxConfig(); i++) 
				{
					GameObject readyScoutNew = GameObject.Instantiate(readyScout, __instance.transform);
					__instance.scoutImages[i] = readyScoutNew.GetComponent<Image>();
				}
			}
			return true;
		}
		
		[HarmonyPatch(typeof(EndScreen), "Initialize")]
		[HarmonyPrefix]
		public static bool InitializePrefix(EndScreen __instance)
		{
			if (__instance.scoutWindows.Length <= 4) 
			{
				int size = LobbyHelper.GetLobbyMaxConfig();

				GameObject scoutWindow = __instance.scoutWindows[0].gameObject;
				GameObject scoutAtPeak = __instance.scoutsAtPeak[0].gameObject;
				GameObject scout = __instance.scouts[0].gameObject;
				GameObject scoutLine = __instance.scoutLines[0].gameObject;

				Array.Resize(ref __instance.scoutWindows, size);
				Array.Resize(ref __instance.scoutsAtPeak, size);
				Array.Resize(ref __instance.scouts, size);
				Array.Resize(ref __instance.scoutLines, size);

				for (int i = 4; i < size; i++) 
				{
					GameObject scoutWindowNew = GameObject.Instantiate(scoutWindow, scoutWindow.transform.parent);
					__instance.scoutWindows[i] = scoutWindowNew.GetComponent<EndScreenScoutWindow>();

					GameObject scoutAtPeakNew = GameObject.Instantiate(scoutAtPeak, scoutAtPeak.transform.parent);
					__instance.scoutsAtPeak[i] = scoutAtPeakNew.GetComponent<Image>();

					GameObject scoutNew = GameObject.Instantiate(scout, scout.transform.parent);
					__instance.scouts[i] = scoutNew.GetComponent<Image>();

					GameObject scoutLineNew = GameObject.Instantiate(scoutLine, scoutLine.transform.parent);
					__instance.scoutLines[i] = scoutLineNew.GetComponent<Transform>();
				}
			}
			return true;
		}

		public static class LobbyHelper 
		{
			public static int GetLobbyMaxConfig()
			{
				return Plugin.MAX_PLAYERS;
			}
		}
	}
}