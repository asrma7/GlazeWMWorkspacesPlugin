using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rainmeter;
using WebSocketSharp;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Linq;
using System.Collections;

// Overview: This is a blank canvas on which to build your plugin.

// Note: GetString, ExecuteBang and an unnamed function for use as a section variable
// have been commented out. If you need GetString, ExecuteBang, and/or section variables 
// and you have read what they are used for from the SDK docs, uncomment the function(s)
// and/or add a function name to use for the section variable function(s). 
// Otherwise leave them commented out (or get rid of them)!

namespace WebSocketClient
{
    class Measure
    {
        Dictionary<string, GlazeWorkspace> workspaces = new Dictionary<string, GlazeWorkspace>();
        WebSocket ws;
        IntPtr Skin;
        String cmdOnOpen;
        String cmdOnWorkspaceChanged;
        String cmdOnError;
        String cmdOnClose;

        static public implicit operator Measure(IntPtr data)
        {
            return (Measure)GCHandle.FromIntPtr(data).Target;
        }

        public T DeserializeJson<T>(string Json)
        {
            JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
            return javaScriptSerializer.Deserialize<T>(Json);
        }


        public void Setup(Rainmeter.API api)
        {
            // Setup WebSocket
            String Address = api.ReadString("Address", "ws://localhost:6123");
            if (!Address.IsNullOrEmpty())
            {
                ws = new WebSocket(Address);
                ws.OnOpen += OnOpen;
                ws.OnMessage += OnMessage;
                ws.OnError += OnError;
                ws.OnClose += OnClose;
                ws.ConnectAsync();
            }

            // Get Commands
            Skin = api.GetSkin();
            cmdOnOpen = api.ReadString("OnOpen", "");
            cmdOnWorkspaceChanged = api.ReadString("OnWorkspaceChanged", "");
            cmdOnError = api.ReadString("OnError", "");
            cmdOnClose = api.ReadString("OnClose", "");
        }

        public void Close()
        {
            if (ws.ReadyState == WebSocketState.Open)
                ws.CloseAsync();
        }

        public void Send(String s)
        {
            if (ws.ReadyState == WebSocketState.Open)
            {
                    ws.SendAsync(s, null);
            }
        }

        public WebSocketState GetState()
        {
            return ws.ReadyState;
        }

        void OnOpen(object sender, EventArgs e)
        {
            ws.Send("query workspaces");
            ws.SendAsync("sub --events all", null);
            API.Execute(Skin, cmdOnOpen);
        }

        void OnMessage(object sender, MessageEventArgs e)
        {
            var jsonResponse = DeserializeJson<Dictionary<string, object>>(e.Data);

            if (jsonResponse.ContainsKey("error") && jsonResponse["error"] != null)
            {
                API.Log(Skin, API.LogType.Error, jsonResponse["error"].ToString());
            }
            else if (jsonResponse["messageType"].ToString() == "client_response")
            {
                //API.Execute(Skin, "[!Log \"Client Response Recieved\"]");
                HandleResponse(jsonResponse["clientMessage"].ToString(), jsonResponse["data"]);
            }
            else if (jsonResponse["messageType"].ToString() == "event_subscription")
            {
                var dataDict = jsonResponse["data"] as Dictionary<string, object>;
                //API.Execute(Skin, "[!Log \"Client Event Recieved\"]");
                HandleEvent(dataDict["eventType"].ToString(), jsonResponse["data"]);
            }
            else
            {
                API.Log(Skin, API.LogType.Debug, "MESSAGE: " + e.Data);
            }
        }

        void OnError(object sender, ErrorEventArgs e)
        {
            //Use regular experssion to replace $Message$ since str.replace can only be case sensitive
            string cmd = Regex.Replace(cmdOnError, "\\$message\\$", e.Message, RegexOptions.IgnoreCase);
            API.Execute(Skin, cmd);
        }

        void OnClose(object sender, CloseEventArgs e)
        {
            //Use regular experssion to replace $Message$ since str.replace can only be case sensitive
            string cmd = Regex.Replace(cmdOnClose, "\\$message\\$", e.Reason, RegexOptions.IgnoreCase);
            API.Execute(Skin, cmd);
        }

        private void HandleResponse(string request, object response)
        {
            var responseDict = response as Dictionary<string, object>;
            switch (request)
            {
                case "query workspaces":
                    {
                        var workspacesList = responseDict["workspaces"] as ArrayList;

                        if (workspacesList != null)
                        {
                            foreach (var workspaceToken in workspacesList)
                            {
                                var workspaceDict = workspaceToken as Dictionary<string, object>;

                                if (workspaceDict != null)
                                {
                                    GlazeWorkspace workspace = new GlazeWorkspace
                                    {
                                        Id = workspaceDict["id"].ToString(),
                                        Name = workspaceDict["name"].ToString(),
                                        ParentId = workspaceDict["parentId"].ToString(),
                                        HasFocus = (bool)workspaceDict["hasFocus"],
                                        IsDisplayed = (bool)workspaceDict["isDisplayed"],
                                        TilingDirection = workspaceDict["tilingDirection"].ToString()
                                    };

                                    if (!string.IsNullOrEmpty(workspace.Id))
                                    {
                                        workspaces[workspace.Id] = workspace;
                                    }
                                }
                            }
                            string cmd = Regex.Replace(cmdOnWorkspaceChanged, "\\$currentWorkspace\\$", GetDisplayedWorkspaceName(), RegexOptions.IgnoreCase);
                            cmd = Regex.Replace(cmd, "\\$totalWorkspaces\\$", GetWorkspaceNameWithHighestValue(), RegexOptions.IgnoreCase);
                            API.Execute(Skin, cmd);
                        }
                    }
                    break;
                default:
                    //Console.WriteLine("Response: " + response);
                    break;
            }
        }

        private void HandleEvent(string eventType, object eventDataObj)
        {
            var eventData = eventDataObj as Dictionary<string, object>;
            switch (eventType)
            {
                case "workspace_activated":
                    {
                        var workspaceDataDict = eventData["activatedWorkspace"] as Dictionary<string, object>;
                        GlazeWorkspace workspaceData = new GlazeWorkspace
                        {
                            Id = workspaceDataDict["id"].ToString(),
                            Name = workspaceDataDict["name"].ToString(),
                            ParentId = workspaceDataDict["parentId"].ToString(),
                            HasFocus = (bool)workspaceDataDict["hasFocus"],
                            IsDisplayed = (bool)workspaceDataDict["isDisplayed"],
                            TilingDirection = workspaceDataDict["tilingDirection"].ToString()
                        };

                        workspaces[workspaceData.Id] = workspaceData;
                    }
                    break;
                case "workspace_deactivated":
                    {
                        string workspaceId = eventData["deactivatedId"].ToString();
                        workspaces.Remove(workspaceId);
                    }
                    break;
                case "focus_changed":
                    {
                        string workspaceId = null;
                        var focusedContainer = eventData["focusedContainer"] as Dictionary<string, object>;

                        if (focusedContainer["type"].ToString() == "window")
                            workspaceId = focusedContainer["parentId"].ToString();
                        else if (focusedContainer["type"].ToString() == "workspace")
                            workspaceId = focusedContainer["id"].ToString();

                        if (workspaceId != null)
                        {
                            string activeWorkspace = GetDisplayedWorkspaceId();
                            if (activeWorkspace != null)
                                workspaces[activeWorkspace].IsDisplayed = false;

                            workspaces[workspaceId].IsDisplayed = true;
                        }
                        string cmd = Regex.Replace(cmdOnWorkspaceChanged, "\\$currentWorkspace\\$", GetDisplayedWorkspaceName(), RegexOptions.IgnoreCase);
                        cmd = Regex.Replace(cmd, "\\$totalWorkspaces\\$", GetWorkspaceNameWithHighestValue(), RegexOptions.IgnoreCase);
                        API.Execute(Skin, cmd);
                    }
                    break;
                default:
                    //Console.WriteLine("EventType: " + eventType);
                    //Console.WriteLine("EventData: " + eventData);
                    break;
            }
        }

        private string GetDisplayedWorkspaceId()
        {
            return workspaces.Values
                     .FirstOrDefault(workspace => workspace.IsDisplayed)
                     ?.Id;
        }

        private string GetDisplayedWorkspaceName()
        {
            return workspaces.Values
                     .FirstOrDefault(workspace => workspace.IsDisplayed)
                     ?.Name;
        }

        public string GetWorkspaceNameWithHighestValue()
        {
            var highestValueWorkspace = workspaces.Values
                .Where(workspace => int.TryParse(workspace.Name, out _))
                .OrderByDescending(workspace => int.Parse(workspace.Name))
                .FirstOrDefault();

            return highestValueWorkspace?.Name;
        }

    }

    public class Plugin
    {
        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            Measure measure = new Measure();
            data = GCHandle.ToIntPtr(GCHandle.Alloc(measure));
            Rainmeter.API api = (Rainmeter.API)rm;
            measure.Setup(api);
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            Measure measure = (Measure)data;
            measure.Close();

            GCHandle.FromIntPtr(data).Free();
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            Measure measure = (Measure)data;
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            Measure measure = (Measure)data;
            double state = (double)measure.GetState();
            return state;
        }

        //[DllExport]
        //public static IntPtr GetString(IntPtr data)
        //{
        //    Measure measure = (Measure)data;
        //
        //    return Marshal.StringToHGlobalUni(""); //returning IntPtr.Zero will result in it not being used
        //}

        [DllExport]
        public static void ExecuteBang(IntPtr data, [MarshalAs(UnmanagedType.LPWStr)] String args)
        {
            Measure measure = (Measure)data;
            measure.Send(args);
        }

        //[DllExport]
        //public static IntPtr (IntPtr data, int argc,
        //    [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 1)] string[] argv)
        //{
        //    Measure measure = (Measure)data;
        //
        //    return Marshal.StringToHGlobalUni(""); //returning IntPtr.Zero will result in it not being used
        //}
    }

    public class GlazeWorkspace
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string ParentId { get; set; }
        public bool HasFocus { get; set; }
        public bool IsDisplayed { get; set; }
        public string TilingDirection { get; set; }
    }
}