using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using Server;
using Server.Gumps;
using Server.Mobiles;
using Server.Network;
using Server.Commands;
using Server.Targets;

namespace Server.GumpForge
{
    public class GumpForgeLink
    {
        private static int Port = 2594;
        private static TcpListener m_Listener;
        private static List<ClientState> m_Clients = new List<ClientState>();
        private static Dictionary<Mobile, string> m_PendingPINs = new Dictionary<Mobile, string>();
        private static Dictionary<Mobile, DateTime> m_PINExpirations = new Dictionary<Mobile, DateTime>();
        private static Dictionary<Mobile, Mobile> m_ActiveSubjects = new Dictionary<Mobile, Mobile>(); // GM -> Subject Player

        public static void Initialize()
        {
            CommandSystem.Register("gf", AccessLevel.Counselor, new CommandEventHandler(Gf_OnCommand));
            CommandSystem.Register("gflink", AccessLevel.Counselor, new CommandEventHandler(Gf_OnCommand));

            try
            {
                m_Listener = new TcpListener(IPAddress.Any, Port);
                m_Listener.Start();
                m_Listener.BeginAcceptTcpClient(OnAccept, null);
                Console.WriteLine("GumpForgeLink: Listening on port {0}", Port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("GumpForgeLink: Error starting server: {0}", ex.Message);
            }
        }

        [Usage("gf")]
        [Description("Triggers GumpForge target cursor or shows connection PIN.")]
        private static void Gf_OnCommand(CommandEventArgs e)
        {
            Mobile from = e.Mobile;

            // Check if GM is already connected via a client
            ClientState client = FindClient(from);
            if (client == null)
            {
                // Generate a one-time PIN
                string pin = Utility.Random(1000, 9999).ToString();
                m_PendingPINs[from] = pin;
                m_PINExpirations[from] = DateTime.UtcNow.AddMinutes(2);

                from.SendMessage(0x55, "[GumpForge] Temporary authentication PIN: {0}", pin);
                from.SendMessage(0x55, "[GumpForge] Enter this PIN in GumpForge editor to connect. Expires in 2 minutes.");
            }
            else
            {
                // Trigger targeting
                from.SendMessage(0x55, "Target a Player to inspect, or an Item to render its gump in GumpForge.");
                from.Target = new GumpForgeTarget();
            }
        }

        private static void OnAccept(IAsyncResult ar)
        {
            try
            {
                TcpClient tcpClient = m_Listener.EndAcceptTcpClient(ar);
                ClientState client = new ClientState(tcpClient);
                lock (m_Clients)
                {
                    m_Clients.Add(client);
                }
                client.Start();
            }
            catch {}

            try
            {
                m_Listener.BeginAcceptTcpClient(OnAccept, null);
            }
            catch {}
        }

        private static ClientState FindClient(Mobile gm)
        {
            lock (m_Clients)
            {
                foreach (ClientState client in m_Clients)
                {
                    if (client.GM == gm && client.IsConnected)
                        return client;
                }
            }
            return null;
        }

        private static void RemoveClient(ClientState client)
        {
            lock (m_Clients)
            {
                m_Clients.Remove(client);
            }
        }

        private class ClientState
        {
            public TcpClient Socket { get; private set; }
            public Mobile GM { get; private set; }
            public bool IsConnected { get; private set; }
            private NetworkStream Stream;
            private Thread ReadThread;

            public ClientState(TcpClient socket)
            {
                Socket = socket;
                IsConnected = true;
            }

            public void Start()
            {
                Stream = Socket.GetStream();
                ReadThread = new Thread(ReadLoop);
                ReadThread.IsBackground = true;
                ReadThread.Start();
            }

            private void ReadLoop()
            {
                byte[] lengthBuffer = new byte[4];
                while (IsConnected)
                {
                    try
                    {
                        // Read length-prefixed packet
                        if (!ReadExactly(Stream, lengthBuffer, 4))
                            break;

                        int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBuffer, 0));
                        if (length <= 0 || length > 1024 * 1024) // 1MB sanity cap
                            break;

                        byte[] payload = new byte[length];
                        if (!ReadExactly(Stream, payload, length))
                            break;

                        byte packetId = payload[0];
                        string json = Encoding.UTF8.GetString(payload, 1, length - 1);

                        HandlePacket(packetId, json);
                    }
                    catch
                    {
                        break;
                    }
                }
                Disconnect();
            }

            private bool ReadExactly(NetworkStream stream, byte[] buffer, int size)
            {
                int totalRead = 0;
                while (totalRead < size)
                {
                    int read = stream.Read(buffer, totalRead, size - totalRead);
                    if (read <= 0) return false;
                    totalRead += read;
                }
                return true;
            }

            private void HandlePacket(byte packetId, string json)
            {
                if (packetId == 0x01) // AuthRequest
                {
                    var matchPin = Regex.Match(json, @"""pin""\s*:\s*""([^""]+)""");
                    if (matchPin.Success)
                    {
                        string enteredPin = matchPin.Groups[1].Value.Trim();
                        Mobile authenticatedGM = null;

                        lock (m_PendingPINs)
                        {
                            foreach (var kvp in m_PendingPINs)
                            {
                                if (kvp.Value == enteredPin && m_PINExpirations[kvp.Key] > DateTime.UtcNow)
                                {
                                    authenticatedGM = kvp.Key;
                                    break;
                                }
                            }

                            if (authenticatedGM != null)
                            {
                                m_PendingPINs.Remove(authenticatedGM);
                                m_PINExpirations.Remove(authenticatedGM);
                            }
                        }

                        if (authenticatedGM != null)
                        {
                            GM = authenticatedGM;
                            SendPacket(0x81, "{\"success\":true,\"gmName\":\"" + GM.Name + "\"}");
                            GM.SendMessage(0x55, "[GumpForge] Connected successfully.");
                        }
                        else
                        {
                            SendPacket(0x81, "{\"success\":false,\"errorMessage\":\"Invalid or expired PIN.\"}");
                            Disconnect();
                        }
                    }
                }
                else if (packetId == 0x02) // RequestTarget
                {
                    if (GM != null)
                    {
                        GM.Target = new GumpForgeTarget();
                        GM.SendMessage(0x55, "Targeting cursor triggered from GumpForge.");
                    }
                }
                else if (packetId == 0x03) // TriggerOnDoubleClick
                {
                    if (GM != null)
                    {
                        var matchPlayer = Regex.Match(json, @"""playerSerial""\s*:\s*(\d+)");
                        var matchItem = Regex.Match(json, @"""itemSerial""\s*:\s*(\d+)");
                        if (matchPlayer.Success && matchItem.Success)
                        {
                            int playerSerial = int.Parse(matchPlayer.Groups[1].Value);
                            int itemSerial = int.Parse(matchItem.Groups[1].Value);

                            Mobile subject = World.FindMobile(playerSerial);
                            Item item = World.FindItem(itemSerial);

                            if (subject != null && item != null)
                            {
                                GM.SendMessage(0x55, "Simulating double-click on {0} as {1}...", item.Name ?? item.GetType().Name, subject.Name);
                                
                                List<Gump> oldGumps = new List<Gump>(subject.Gumps);
                                item.OnDoubleClick(subject);

                                Gump newGump = null;
                                foreach (Gump g in subject.Gumps)
                                {
                                    if (!oldGumps.Contains(g))
                                    {
                                        newGump = g;
                                        break;
                                    }
                                }

                                if (newGump != null)
                                {
                                    string gumpJson = GumpSerializer.Serialize(newGump);
                                    SendPacket(0x83, gumpJson);

                                    // Close on player to keep it clean, if it is different from GM
                                    if (subject != GM)
                                    {
                                        subject.CloseGump(newGump.GetType());
                                    }
                                }
                                else
                                {
                                    SendPacket(0x84, "{\"message\":\"No gump was displayed for this item.\"}");
                                }
                            }
                        }
                    }
                }
            }

            public void SendPacket(byte packetId, string json)
            {
                if (!IsConnected) return;

                try
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length + 1));
                    
                    Stream.Write(lengthPrefix, 0, 4);
                    Stream.WriteByte(packetId);
                    Stream.Write(payload, 0, payload.Length);
                    Stream.Flush();
                }
                catch
                {
                    Disconnect();
                }
            }

            public void Disconnect()
            {
                if (!IsConnected) return;
                IsConnected = false;

                try { Stream?.Close(); } catch {}
                try { Socket?.Close(); } catch {}

                RemoveClient(this);

                if (GM != null)
                {
                    GM.SendMessage(0x22, "[GumpForge] Disconnected from editor.");
                }
            }
        }

        private class GumpForgeTarget : Target
        {
            public GumpForgeTarget() : base(-1, false, TargetFlags.None)
            {
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                ClientState client = FindClient(from);
                if (client == null) return;

                if (targeted is Mobile player)
                {
                    m_ActiveSubjects[from] = player;
                    from.SendMessage(0x55, "Targeted player: {0}. Retracted stats/skills. Subject set in GumpForge.", player.Name);
                    
                    string playerJson = PlayerSerializer.Serialize(player);
                    client.SendPacket(0x82, playerJson);
                }
                else if (targeted is Item item)
                {
                    Mobile subject = from;
                    if (m_ActiveSubjects.TryGetValue(from, out Mobile activeSub) && activeSub != null && !activeSub.Deleted && activeSub.Map != Map.Internal)
                    {
                        subject = activeSub;
                    }

                    from.SendMessage(0x55, "Targeted item: {0}. Opening as {1}...", item.Name ?? item.GetType().Name, subject.Name);

                    List<Gump> oldGumps = new List<Gump>(subject.Gumps);
                    item.OnDoubleClick(subject);

                    Gump newGump = null;
                    foreach (Gump g in subject.Gumps)
                    {
                        if (!oldGumps.Contains(g))
                        {
                            newGump = g;
                            break;
                        }
                    }

                    if (newGump != null)
                    {
                        string gumpJson = GumpSerializer.Serialize(newGump);
                        client.SendPacket(0x83, gumpJson);

                        if (subject != from)
                        {
                            subject.CloseGump(newGump.GetType());
                        }
                    }
                    else
                    {
                        client.SendPacket(0x84, "{\"message\":\"No gump was displayed for this item.\"}");
                    }
                }
            }
        }

        private static class PlayerSerializer
        {
            private static readonly Regex SensitiveRegex = new Regex(
                @"(password|hash|salt|account|username|ip|address|socket|netstate|session|token|auth|key|secret)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            public static string Serialize(Mobile m)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                sb.AppendFormat("\"name\":\"{0}\",", Escape(m.Name));
                sb.AppendFormat("\"serial\":{0},", m.Serial.Value);
                sb.AppendFormat("\"type\":\"Player\",");
                sb.Append("\"properties\":[");

                List<string> props = new List<string>();
                
                // Add standard fields safely
                AddProp(props, "Name", m.Name);
                AddProp(props, "Serial", m.Serial.Value.ToString());
                AddProp(props, "AccessLevel", m.AccessLevel.ToString());
                AddProp(props, "Map", m.Map?.ToString() ?? "Internal");
                AddProp(props, "Location", m.Location.ToString());
                AddProp(props, "Hits", string.Format("{0}/{1}", m.Hits, m.HitsMax));
                AddProp(props, "Stamina", string.Format("{0}/{1}", m.Stam, m.StamMax));
                AddProp(props, "Mana", string.Format("{0}/{1}", m.Mana, m.ManaMax));
                AddProp(props, "Strength", m.Str.ToString());
                AddProp(props, "Dexterity", m.Dex.ToString());
                AddProp(props, "Intelligence", m.Int.ToString());

                // Reflect custom fields/properties
                Type t = m.GetType();
                foreach (PropertyInfo prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!prop.CanRead) continue;
                    if (SensitiveRegex.IsMatch(prop.Name)) continue; // Account safety blacklist

                    Type pType = prop.PropertyType;
                    if (pType == typeof(int) || pType == typeof(bool) || pType == typeof(string) || pType == typeof(double) || pType.IsEnum)
                    {
                        try
                        {
                            object val = prop.GetValue(m, null);
                            if (val != null)
                            {
                                AddProp(props, prop.Name, val.ToString());
                            }
                        }
                        catch {}
                    }
                }

                sb.Append(string.Join(",", props.ToArray()));
                sb.Append("]}");
                return sb.ToString();
            }

            private static void AddProp(List<string> list, string name, string value)
            {
                list.Add("{\"name\":\"" + Escape(name) + "\",\"value\":\"" + Escape(value) + "\"}");
            }
        }

        private static class GumpSerializer
        {
            public static string Serialize(Gump g)
            {
                var sb = new StringBuilder();
                sb.Append("{");
                sb.AppendFormat("\"gumpClassName\":\"{0}\",", g.GetType().Name);
                sb.AppendFormat("\"gumpX\":{0},", g.X);
                sb.AppendFormat("\"gumpY\":{0},", g.Y);
                sb.AppendFormat("\"isDraggable\":{0},", g.Draggable.ToString().ToLower());
                sb.AppendFormat("\"isClosable\":{0},", g.Closable.ToString().ToLower());
                sb.AppendFormat("\"isResizable\":{0},", g.Resizable.ToString().ToLower());
                sb.AppendFormat("\"isDisposable\":{0},", g.Disposable.ToString().ToLower());
                sb.Append("\"pages\":[");

                // UO gumps are composed of a linear set of entries. GumpPage entries toggle the page context.
                // We will group them into pages.
                Dictionary<int, List<string>> pageElements = new Dictionary<int, List<string>>();
                pageElements[0] = new List<string>();

                int currentPage = 0;

                for (int i = 0; i < g.Entries.Count; i++)
                {
                    GumpEntry entry = g.Entries[i];
                    if (entry is GumpPage gp)
                    {
                        currentPage = gp.Page;
                        if (!pageElements.ContainsKey(currentPage))
                        {
                            pageElements[currentPage] = new List<string>();
                        }
                        continue;
                    }

                    string elementJson = SerializeEntry(entry, g);
                    if (!string.IsNullOrEmpty(elementJson))
                    {
                        pageElements[currentPage].Add(elementJson);
                    }
                }

                List<string> pageList = new List<string>();
                foreach (var kvp in pageElements)
                {
                    if (kvp.Key == 0 || kvp.Value.Count > 0)
                    {
                        pageList.Add("{\"pageNumber\":" + kvp.Key + ",\"name\":\"Page " + kvp.Key + "\",\"elements\":[" + string.Join(",", kvp.Value.ToArray()) + "]}");
                    }
                }

                sb.Append(string.Join(",", pageList.ToArray()));
                sb.Append("]}");
                return sb.ToString();
            }

            private static string SerializeEntry(GumpEntry entry, Gump gump)
            {
                string type = entry.GetType().Name;
                
                // Strip namespace if present
                int dot = type.LastIndexOf('.');
                if (dot >= 0) type = type.Substring(dot + 1);

                // Strip "Gump" prefix/suffix
                if (type.StartsWith("Gump")) type = type.Substring(4);

                var sb = new StringBuilder();
                sb.Append("{");
                sb.AppendFormat("\"type\":\"{0}\",", type);
                sb.AppendFormat("\"id\":\"{0}\",", Guid.NewGuid().ToString());
                sb.AppendFormat("\"name\":\"{0}_{1}\",", type, Utility.Random(100, 999));
                sb.AppendFormat("\"x\":{0},", GetProp(entry, "X") ?? 0);
                sb.AppendFormat("\"y\":{0},", GetProp(entry, "Y") ?? 0);
                sb.AppendFormat("\"width\":{0},", GetProp(entry, "Width") ?? 0);
                sb.AppendFormat("\"height\":{0},", GetProp(entry, "Height") ?? 0);
                sb.Append("\"properties\":{");

                List<string> props = new List<string>();

                if (type == "Background")
                {
                    props.Add(FormatInt("gumpId", GetProp(entry, "GumpID")));
                }
                else if (type == "Image")
                {
                    props.Add(FormatInt("gumpId", GetProp(entry, "GumpID")));
                    props.Add(FormatInt("hue", GetProp(entry, "Hue")));
                }
                else if (type == "ImageTiled")
                {
                    props.Add(FormatInt("gumpId", GetProp(entry, "GumpID")));
                }
                else if (type == "Button")
                {
                    props.Add(FormatInt("normalId", GetProp(entry, "NormalID")));
                    props.Add(FormatInt("pressedId", GetProp(entry, "PressedID")));
                    props.Add(FormatInt("buttonId", GetProp(entry, "ButtonID")));
                    props.Add(FormatString("buttonType", (GetProp(entry, "Type") ?? "").ToString()));
                    props.Add(FormatInt("param", GetProp(entry, "Param")));
                }
                else if (type == "Check")
                {
                    props.Add(FormatInt("inactiveId", GetProp(entry, "InactiveID")));
                    props.Add(FormatInt("activeId", GetProp(entry, "ActiveID")));
                    props.Add(FormatInt("switchId", GetProp(entry, "SwitchID")));
                    props.Add(FormatBool("initialState", GetProp(entry, "InitialState")));
                }
                else if (type == "Radio")
                {
                    props.Add(FormatInt("inactiveId", GetProp(entry, "InactiveID")));
                    props.Add(FormatInt("activeId", GetProp(entry, "ActiveID")));
                    props.Add(FormatInt("groupId", GetProp(entry, "GroupID")));
                    props.Add(FormatInt("switchId", GetProp(entry, "SwitchID")));
                    props.Add(FormatBool("initialState", GetProp(entry, "InitialState")));
                }
                else if (type == "Label")
                {
                    string labelText = "";
                    object textVal = GetProp(entry, "Text");
                    if (textVal is int index && index >= 0 && index < gump.Texts.Count)
                        labelText = gump.Texts[index];
                    else if (textVal is string s)
                        labelText = s;

                    props.Add(FormatString("text", labelText));
                    props.Add(FormatInt("hue", GetProp(entry, "Hue")));
                    props.Add(FormatInt("font", GetProp(entry, "Font")));
                }
                else if (type == "LabelCropped")
                {
                    string labelText = "";
                    object textVal = GetProp(entry, "Text");
                    if (textVal is int index && index >= 0 && index < gump.Texts.Count)
                        labelText = gump.Texts[index];
                    else if (textVal is string s)
                        labelText = s;

                    props.Add(FormatString("text", labelText));
                    props.Add(FormatInt("hue", GetProp(entry, "Hue")));
                }
                else if (type == "Html")
                {
                    string labelText = "";
                    object textVal = GetProp(entry, "Text");
                    if (textVal is int index && index >= 0 && index < gump.Texts.Count)
                        labelText = gump.Texts[index];
                    else if (textVal is string s)
                        labelText = s;

                    props.Add(FormatString("text", labelText));
                    props.Add(FormatBool("hasBackground", GetProp(entry, "Background")));
                    props.Add(FormatBool("hasScrollbar", GetProp(entry, "Scrollbar")));
                }
                else if (type == "HtmlLocalized")
                {
                    props.Add(FormatInt("clilocId", GetProp(entry, "Number")));
                    props.Add(FormatString("args", (GetProp(entry, "Arguments") ?? GetProp(entry, "Args") ?? "").ToString()));
                    props.Add(FormatInt("color", GetProp(entry, "Color")));
                    props.Add(FormatBool("hasBackground", GetProp(entry, "Background")));
                    props.Add(FormatBool("hasScrollbar", GetProp(entry, "Scrollbar")));
                }
                else if (type == "TextEntry")
                {
                    string initialText = "";
                    object textVal = GetProp(entry, "InitialText");
                    if (textVal is int index && index >= 0 && index < gump.Texts.Count)
                        initialText = gump.Texts[index];
                    else if (textVal is string s)
                        initialText = s;

                    props.Add(FormatInt("entryId", GetProp(entry, "EntryID")));
                    props.Add(FormatString("initialText", initialText));
                    props.Add(FormatInt("hue", GetProp(entry, "Hue")));
                    props.Add(FormatInt("maxLength", GetProp(entry, "Size") ?? GetProp(entry, "MaxLength")));
                }
                else if (type == "Item")
                {
                    props.Add(FormatInt("itemId", GetProp(entry, "ItemID")));
                    props.Add(FormatInt("hue", GetProp(entry, "Hue")));
                }
                else if (type == "Tooltip")
                {
                    props.Add(FormatInt("clilocId", GetProp(entry, "Number")));
                }

                // Clean null/empty values
                props.RemoveAll(string.IsNullOrEmpty);

                sb.Append(string.Join(",", props.ToArray()));
                sb.Append("}}");
                return sb.ToString();
            }

            private static string FormatInt(string key, object val) => val == null ? "" : "\"" + key + "\":" + val.ToString();
            private static string FormatBool(string key, object val) => val == null ? "" : "\"" + key + "\":" + val.ToString().ToLower();
            private static string FormatString(string key, string val) => "\"" + key + "\":\"" + Escape(val) + "\"";

            private static object GetProp(object obj, string name)
            {
                if (obj == null) return null;
                Type t = obj.GetType();
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj, null);
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
                return null;
            }
        }

        private static string Escape(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
