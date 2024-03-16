using HarmonyLib;
using System.Reflection;
using UnityEngine;
using System.Linq;
using System;
using BepInEx;
using BepInEx.Logging;
using System.Runtime.InteropServices;
using FMODUnity;
using FMOD.Studio;
using System.IO;

namespace Azim.PlasmaSoundAPI
{
    public class BankLoadException : Exception 
    {
        public BankLoadException(string message) : base(message) { }
    }

    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class PlasmaSoundAPI : BaseUnityPlugin
    {
        internal static ManualLogSource mls;
        internal static FMOD.Studio.EVENT_CALLBACK dialogueCallback;
        private static bool loaded = false;

        // 901a29b5-0282-40f2-9738-5661c7ffbaf2 - master bus 
        internal static readonly string sound2d = "{2e2adb70-14df-4219-bb0d-b5c6ffd8fd4f}"; //action guid for 2d sound
        internal static readonly string sound3d = "{6ced009f-0452-428d-9d94-994ae51660fc}"; //action guid for 3d sound


        private void Awake()
        {
            mls = base.Logger;
            mls.LogInfo("Starting initialization of PlasmaSoundAPI");
            
            /*
            foreach(string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                mls.LogInfo("Found embedded resource: " + name);
            }
            */
            
            Harmony harmony = new Harmony("PlasmaSoundAPI");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            mls.LogInfo("PlasmaSoundAPI initialized successfully");
        }

        public static EventInstance PlaySound2D(AudioClip audioclip)
        {
            return PlayClipInFmod(audioclip, false, new());
        }
        public static EventInstance PlaySound3D(AudioClip audioclip, Vector3 position)
        {
            return PlayClipInFmod(audioclip, true, position);
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(AudioController), "Init")]
        internal static void loadDummyBank()
        {
            if (loaded) return;

            dialogueCallback = new FMOD.Studio.EVENT_CALLBACK(PlayFileCallBackUsingAudioFile);
            byte[] bankMemory = ReadFully(Assembly.GetExecutingAssembly().GetManifestResourceStream("Azim.PlasmaSoundAPI.Modded.bank"));
            var result = RuntimeManager.StudioSystem.loadBankMemory(bankMemory, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out Bank bank);
            mls.LogInfo("Load bank result: " + result);
            
            if(result != FMOD.RESULT.OK)
            {
                mls.LogError("Dummy sound bank not loaded, aborting");
                throw new BankLoadException("Dummy tank not loaded");
            }
            
            bank.getEventList(out EventDescription[] events);
            
            foreach(EventDescription descr in events)
            {
                descr.getPath(out string path);
                descr.getID(out Guid guid);
                descr.is3D(out bool is3D);

                //mls.LogInfo(path + "|" + guid.ToString() + "|" + is3D);
            }
            loaded = true;
        }

        internal static EventInstance PlayClipInFmod(AudioClip audioclip, bool is3d, Vector3 position)
        {
            if (!loaded)
            {
                loadDummyBank();
            }

            float[] audioclip_data = new float[audioclip.samples * audioclip.channels];
            audioclip.GetData(audioclip_data, 0);

            // We no longer require the 'key' parameter as it was used to access the FMOD audio table. 
            // string key = audioclip.name; 

            // We are getting the information out of the Unity Audio clip passed into the function 
            SoundRequirements sound_requirements = new SoundRequirements(
                // The name of the clip we are playing, makes it easier to identify when in code  
                audioclip.name,
                // Parameters required to create sound exit info: https://fmod.com/docs/2.02/api/core-api-system.html#fmod_createsoundexinfo
                audioclip.samples,
                audioclip.channels,
                FMOD.SOUND_FORMAT.PCMFLOAT,
                audioclip.frequency,
                // The sample data that will be copied into the sound when we create it 
                audioclip_data);

            EventInstance soundInstance = FMODUnity.RuntimeManager.CreateInstance(is3d ? sound3d : sound2d);


            // Pin the key string in memory and pass a pointer through the user data
            GCHandle stringHandle = GCHandle.Alloc(sound_requirements);
            soundInstance.setUserData(GCHandle.ToIntPtr(stringHandle));
            
            if (is3d)
            {
                soundInstance.set3DAttributes(position.To3DAttributes());
            }

            soundInstance.setCallback(dialogueCallback);
            var result = soundInstance.start();
            //mls.LogInfo(result);
            result = soundInstance.release();
            return soundInstance;
        }

        // adapted from fmod programmer sound example
        [AOT.MonoPInvokeCallback(typeof(FMOD.Studio.EVENT_CALLBACK))]
        internal static FMOD.RESULT PlayFileCallBackUsingAudioFile(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr instPrt, IntPtr paramsPrt)
        {
            //mls.LogInfo("PlayFileCallBackUsingAudioFile event type " + type.ToString());

            FMOD.Studio.EventInstance inst = new FMOD.Studio.EventInstance(instPrt);

            if (!inst.isValid())
            {
                return FMOD.RESULT.ERR_EVENT_NOTFOUND;
            }

            // Retrieving the user data from the instance
            inst.getUserData(out IntPtr clipDataPtr);
            GCHandle clipHandle = GCHandle.FromIntPtr(clipDataPtr);
            // Assinging the our data to a new struct so we can access all the information 
            SoundRequirements clip = clipHandle.Target as SoundRequirements;

            // Depending on the callback type will decide what happens next 
            switch (type)
            {
                case FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND:
                    {
                        // This is what we will use to pass the sound back out to our instance 
                        var param = (FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(paramsPrt, typeof(FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES));

                        // Retrieve the masterGroup, or the channel group you wish to play the clip too 
                        ERRCHECK(FMODUnity.RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup masterGroup), "Failed to get masterGroup from core system");

                        // Calculating the length of the audio clip by the samples and channels 
                        uint lenBytes = (uint)(clip.samples * clip.channels * sizeof(float));

                        // Sound exit info to be used when creating the sound 
                        FMOD.CREATESOUNDEXINFO soundInfo = new FMOD.CREATESOUNDEXINFO();
                        soundInfo.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
                        soundInfo.length = lenBytes;
                        soundInfo.format = FMOD.SOUND_FORMAT.PCMFLOAT;
                        soundInfo.defaultfrequency = clip.defaultFrequency;
                        soundInfo.numchannels = clip.channels;

                        // Creating the sound using soundInfo
                        FMOD.RESULT result = ERRCHECK(FMODUnity.RuntimeManager.CoreSystem.createSound(clip.name, FMOD.MODE.OPENUSER, ref soundInfo, out FMOD.Sound sound), "Failed to create sound");
                        if (result != FMOD.RESULT.OK)
                            return result;

                        // Now we have created our sound, we need to give it the sample data from the audio clip 
                        result = ERRCHECK(sound.@lock(0, lenBytes, out IntPtr ptr1, out IntPtr ptr2, out uint len1, out uint len2), "Failed to lock sound");
                        if (result != FMOD.RESULT.OK)
                            return result;

                        Marshal.Copy(clip.sampleData, 0, ptr1, (int)(len1 / sizeof(float)));
                        if (len2 > 0)
                            Marshal.Copy(clip.sampleData, (int)(len1 / sizeof(float)), ptr2, (int)(len2 / sizeof(float)));

                        result = ERRCHECK(sound.unlock(ptr1, ptr2, len1, len2), "Failed to unlock sound");
                        if (result != FMOD.RESULT.OK)
                            return result;

                        ERRCHECK(sound.setMode(FMOD.MODE.DEFAULT), "Failed to set the sound mode");


                        if (result == FMOD.RESULT.OK)
                        {
                            param.sound = sound.handle;
                            param.subsoundIndex = -1;
                            // Passing the sound back out again to be played 
                            Marshal.StructureToPtr(param, paramsPrt, false);
                        }
                        else
                            return result;
                    }
                    break;
                case FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND:
                    {
                        var param = (FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES)Marshal.PtrToStructure(paramsPrt, typeof(FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES));
                        var sound = new FMOD.Sound(param.sound);
                        FMOD.RESULT result = ERRCHECK(sound.release(), "Failed to release sound");
                        if (result != FMOD.RESULT.OK)
                            return result;
                    }
                    break;
                case FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED:
                    clipHandle.Free();
                    break;
            }
            return FMOD.RESULT.OK;
        }
        private static FMOD.RESULT ERRCHECK(FMOD.RESULT result, string failMsg)
        {
            if (result != FMOD.RESULT.OK)
            {
                mls.LogWarning(failMsg + " with result: " + result);
            }
            return result;
        }
        private static byte[] ReadFully(Stream input)
        {
            if(input == null)
            {
                throw new InvalidDataException("Stream is null");
            }

            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }

    internal class SoundRequirements
    {
        public string name;
        public int samples;
        public int channels;
        public FMOD.SOUND_FORMAT format;
        public int defaultFrequency;
        public float[] sampleData;

        public SoundRequirements(string name, int samples, int channel, FMOD.SOUND_FORMAT format, int defaultFrequency, float[] sampleData)
        {
            this.name = name;
            this.samples = samples;
            this.channels = channel;
            this.format = format;
            this.defaultFrequency = defaultFrequency;
            this.sampleData = sampleData;
        }
    }
}
