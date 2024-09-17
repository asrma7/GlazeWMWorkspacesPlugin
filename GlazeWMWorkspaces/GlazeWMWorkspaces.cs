using System;
using System.Runtime.InteropServices;
using Rainmeter;

namespace GlazeWMWorkspaces
{
    class Measure
    {
        static public implicit operator Measure(IntPtr data)
        {
            return (Measure)GCHandle.FromIntPtr(data).Target;
        }
        public string myCommand = "";
        public int updateRate;
        public DateTime timer;

        public Rainmeter.API api;
    }

    public class Plugin
    {
        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            Measure measure = new Measure();
            Rainmeter.API api = (Rainmeter.API)rm;

            measure.api = api;

            //We do this in Initialize so that if a skin were to turn on DynamicVariables it will not break the plugin
            measure.timer = DateTime.UtcNow;

            data = GCHandle.ToIntPtr(GCHandle.Alloc(measure));

            measure.api.Execute("[!Log \"This is log from Execute\"]");
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            Measure measure = (Measure)data;
            Rainmeter.API api = (Rainmeter.API)rm;

            measure.updateRate = api.ReadInt("Timer", 1);
            //We dont have to replace measures here as they will be replaced during Execute so we can pass false. 
            //Note though that doing that measures will always then have their current info in update but variables will not. See the commented out code below to have both always act like DynamicVariables=1
            measure.myCommand = api.ReadString("OnTimer", "", false);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            Measure measure = (Measure)data;

            int timePassed = (int)(DateTime.UtcNow - measure.timer).TotalSeconds;

            if (timePassed >= measure.updateRate)
            {
                //We could also do something like this to get DynamicVariables without the need to turn them on. Uncomment this to see the second example work
                //measure.myCommand = measure.api.ReadString("OnTimer", "", false);

                measure.api.Execute(measure.myCommand);
                measure.timer = DateTime.UtcNow;
            }

            //Even if you don't plan for users to use it returning a value can be useful for helping with skin debugging
            return measure.updateRate - timePassed;
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            GCHandle.FromIntPtr(data).Free();
        }
    }
}
