using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using Server;
using Server.Mobiles;
using Server.Items;
using Server.Commands;
using Server.Targeting;
using Server.Accounting;
using System.Reflection;

namespace Server.GumpForge
{
    public class GumpForgeLink
    {
        private static TcpListener m_Listener;
        private static List<LinkConnection> m_Connections = new List<LinkConnection>();

        public static void Initialize()
        {
            CommandSystem.Register("GumpForge", AccessLevel.GameMaster, new CommandEventHandler(GumpForge_OnCommand));
            CommandSystem.Register("gf", AccessLevel.GameMaster, new CommandEventHandler(GumpForge_OnCommand));

            try
            {
                // Listen on port 2595 (per port settings updated by user)
                m_Listener = new TcpListener(IPAddress.Any, 2595);
                m_Listener.Start();
                m_Listener.BeginAcceptTcpClient(new AsyncCallback(OnAccept), null);
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("[GumpForgeLink] Listening on port 2595 for client links");
                Console.WriteLine("[GumpForgeLink] Authenticate using staff Account credentials.");
                Console.WriteLine("--------------------------------------------------");
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("[GumpForgeLink] Error starting server: {0}", e.Message));
            }
        }

        [Usage("GumpForge")]
        [Description("Binds target cursor to serialize a gump or player context to GumpForge editor.")]
        private static void GumpForge_OnCommand(CommandEventArgs e)
        {
            Mobile from = e.Mobile;
            Account acct = from.Account as Account;
            if (acct == null) return;

            LinkConnection activeConn = null;
            lock (m_Connections)
            {
                foreach (LinkConnection conn in m_Connections)
                {
                    if (conn.IsAuthenticated && conn.Account == acct)
                    {
                        activeConn = conn;
                        break;
                    }
                }
            }

            if (activeConn == null)
            {
                from.SendMessage(0x22, "You must connect GumpForge client and log in with this staff account first!");
                return;
            }

            from.Target = new GumpForgeTarget(activeConn);
            from.SendMessage(0x5a, "Target a player to load their context, or target an item to fetch its gump menu.");
        }

        private static void OnAccept(IAsyncResult ar)
        {
            try
            {
                TcpClient client = m_Listener.EndAcceptTcpClient(ar);
                lock (m_Connections)
                {
                    m_Connections.Add(new LinkConnection(client));
                }
                m_Listener.BeginAcceptTcpClient(new AsyncCallback(OnAccept), null);
            }
            catch { }
        }

        private class GumpForgeTarget : Target
        {
            private LinkConnection m_Connection;

            public GumpForgeTarget(LinkConnection conn) : base(12, false, TargetFlags.None)
            {
                m_Connection = conn;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                if (!m_Connection.IsConnected)
                {
                    from.SendMessage(0x22, "The GumpForge link connection was lost.");
                    return;
                }

                Mobile targetedMobile = targeted as Mobile;
                if (targetedMobile != null)
                {
                    from.SendMessage(0x5a, string.Format("[GumpForge] Targeted player {0}. Reflected properties serialized.", targetedMobile.Name));
                    
                    var props = GetReflectedProperties(targetedMobile);
                    props.Add(new { name = "TargetType", value = "Player" });
                    var packet = new
                    {
                        success = true,
                        name = targetedMobile.Name,
                        serial = targetedMobile.Serial.Value,
                        properties = props
                    };
                    m_Connection.SendPacket(0x82, SerializeJson(packet));
                }
                else
                {
                    Item targetedItem = targeted as Item;
                    if (targetedItem != null)
                    {
                        from.SendMessage(0x5a, string.Format("[GumpForge] Targeted item {0}. Serializing properties.", targetedItem.GetType().Name));

                        var props = GetReflectedProperties(targetedItem);
                        props.Add(new { name = "TargetType", value = "Item" });
                        var packet = new
                        {
                            success = true,
                            name = targetedItem.GetType().Name,
                            serial = targetedItem.Serial.Value,
                            properties = props
                        };
                        m_Connection.SendPacket(0x82, SerializeJson(packet));

                        // For demo purposes, we send the Spellbook gump if they target a spellbook
                        if (targetedItem is Spellbook)
                        {
                            SendMockSpellbookGump(m_Connection);
                        }
                    }
                }
            }
        }

        private static List<object> GetReflectedProperties(object obj)
        {
            var list = new List<object>();
            if (obj == null) return list;

            Type type = obj.GetType();
            PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in props)
            {
                try
                {
                    object val = prop.GetValue(obj, null);
                    string sVal = CleanValue(prop.Name, val);
                    list.Add(new { name = prop.Name, value = sVal });
                }
                catch { }
            }

            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                try
                {
                    object val = field.GetValue(obj);
                    string sVal = CleanValue(field.Name, val);
                    list.Add(new { name = field.Name, value = sVal });
                }
                catch { }
            }

            return list;
        }

        private static string CleanValue(string key, object val)
        {
            if (val == null) return "null";
            string sVal = val.ToString();
            string keyLower = key.ToLowerInvariant();
            
            // SECURITY FILTER: Mask all sensitive properties
            if (keyLower.Contains("pass") || keyLower.Contains("password") || keyLower.Contains("acctname") || keyLower.Contains("accountname"))
            {
                return "[PROTECTED]";
            }
            return sVal;
        }

        private static void SendMockSpellbookGump(LinkConnection conn)
        {
            var mockGump = new
            {
                success = true,
                gump = "{\"gumpClassName\":\"SpellbookGump\",\"gumpX\":100,\"gumpY\":100,\"isDraggable\":true,\"isClosable\":true,\"isResizable\":false,\"canvasWidth\":400,\"canvasHeight\":300,\"pages\":[{\"pageNumber\":0,\"name\":\"Index\",\"elements\":[{\"type\":\"Background\",\"properties\":{\"gumpId\":9270,\"width\":400,\"height\":300}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":30,\"text\":\"MAGERY SPELLBOOK\",\"hue\":30,\"font\":0}},{\"type\":\"Button\",\"properties\":{\"x\":330,\"y\":250,\"normalId\":2235,\"pressedId\":2236,\"buttonId\":2,\"buttonType\":\"Page\",\"param\":1,\"name\":\"NextButton\"}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":80,\"text\":\"Press button to open Circle 1:\",\"hue\":0,\"font\":0}},{\"type\":\"Button\",\"properties\":{\"x\":50,\"y\":120,\"normalId\":2235,\"pressedId\":2236,\"buttonId\":10,\"buttonType\":\"Page\",\"param\":1,\"name\":\"Circle1Button\"}},{\"type\":\"Label\",\"properties\":{\"x\":80,\"y\":120,\"text\":\"First Circle Spells\",\"hue\":0,\"font\":0}},{\"type\":\"Check\",\"properties\":{\"x\":50,\"y\":160,\"inactiveId\":210,\"activeId\":211,\"switchId\":1,\"initialState\":true,\"name\":\"ClumsySpell\"}},{\"type\":\"Label\",\"properties\":{\"x\":85,\"y\":160,\"text\":\"Include Clumsy Scroll\",\"hue\":0,\"font\":0}}]},{\"pageNumber\":1,\"name\":\"Circle 1\",\"elements\":[{\"type\":\"Background\",\"properties\":{\"gumpId\":9270,\"width\":400,\"height\":300}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":30,\"text\":\"FIRST CIRCLE SPELLS\",\"hue\":30,\"font\":0}},{\"type\":\"Button\",\"properties\":{\"x\":50,\"y\":250,\"normalId\":2235,\"pressedId\":2236,\"buttonId\":3,\"buttonType\":\"Page\",\"param\":0,\"name\":\"BackButton\"}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":80,\"text\":\"1. Clumsy\",\"hue\":0,\"font\":0}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":110,\"text\":\"2. Feeblemind\",\"hue\":0,\"font\":0}},{\"type\":\"Label\",\"properties\":{\"x\":50,\"y\":140,\"text\":\"3. Heal\",\"hue\":0,\"font\":0}}]}]}"
            };
            conn.SendPacket(0x83, SerializeJson(mockGump));
        }

        private static string GetJsonValue(string json, string key)
        {
            string keyPat = "\"" + key + "\":\"";
            int idx = json.IndexOf(keyPat);
            if (idx == -1)
            {
                keyPat = "\"" + key + "\":";
                idx = json.IndexOf(keyPat);
                if (idx == -1) return string.Empty;
                int start = idx + keyPat.Length;
                int end = json.IndexOf(",", start);
                if (end == -1) end = json.IndexOf("}", start);
                if (end == -1) return string.Empty;
                return json.Substring(start, end - start).Trim('\"', ' ', '}');
            }
            else
            {
                int start = idx + keyPat.Length;
                int end = json.IndexOf("\"", start);
                if (end == -1) return string.Empty;
                return json.Substring(start, end - start);
            }
        }

        private static string SerializeJson(object obj)
        {
            StringBuilder sb = new StringBuilder();
            SerializeValue(sb, obj);
            return sb.ToString();
        }

        private static void SerializeValue(StringBuilder sb, object val)
        {
            if (val == null)
            {
                sb.Append("null");
                return;
            }

            Type t = val.GetType();
            if (t == typeof(string))
            {
                sb.Append("\"").Append(val.ToString().Replace("\"", "\\\"")).Append("\"");
            }
            else if (t == typeof(bool))
            {
                sb.Append((bool)val ? "true" : "false");
            }
            else if (t.IsPrimitive || t == typeof(int) || t == typeof(double) || t == typeof(float) || t == typeof(long))
            {
                sb.Append(val.ToString());
            }
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                sb.Append("[");
                var list = (System.Collections.IList)val;
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    SerializeValue(sb, list[i]);
                }
                sb.Append("]");
            }
            else
            {
                sb.Append("{");
                PropertyInfo[] props = t.GetProperties();
                FieldInfo[] fields = t.GetFields();
                int idx = 0;

                foreach (var prop in props)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append("\"").Append(prop.Name).Append("\":");
                    SerializeValue(sb, prop.GetValue(val, null));
                    idx++;
                }

                foreach (var field in fields)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append("\"").Append(field.Name).Append("\":");
                    SerializeValue(sb, field.GetValue(val));
                    idx++;
                }
                sb.Append("}");
            }
        }

        private class LinkConnection
        {
            private TcpClient m_Client;
            private NetworkStream m_Stream;
            private bool m_IsAuthenticated;
            private byte[] m_Buffer = new byte[4096];
            private MemoryStream m_IncomingBuffer = new MemoryStream();
            private Account m_Account;

            public bool IsConnected { get { return m_Client != null && m_Client.Connected; } }
            public bool IsAuthenticated { get { return m_IsAuthenticated; } }
            public Account Account { get { return m_Account; } }

            public LinkConnection(TcpClient client)
            {
                m_Client = client;
                m_Stream = client.GetStream();
                m_Stream.BeginRead(m_Buffer, 0, m_Buffer.Length, new AsyncCallback(OnRead), null);
            }

            private void OnRead(IAsyncResult ar)
            {
                try
                {
                    int bytesRead = m_Stream.EndRead(ar);
                    if (bytesRead <= 0)
                    {
                        Disconnect();
                        return;
                    }

                    m_IncomingBuffer.Write(m_Buffer, 0, bytesRead);
                    ParsePackets();

                    if (IsConnected)
                    {
                        m_Stream.BeginRead(m_Buffer, 0, m_Buffer.Length, new AsyncCallback(OnRead), null);
                    }
                }
                catch
                {
                    Disconnect();
                }
            }

            private void ParsePackets()
            {
                byte[] data = m_IncomingBuffer.ToArray();
                int offset = 0;

                while (data.Length - offset >= 5)
                {
                    int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(data, offset));
                    if (length <= 0 || length > 10 * 1024 * 1024)
                    {
                        Disconnect();
                        return;
                    }

                    if (data.Length - offset - 4 < length)
                    {
                        break;
                    }

                    byte packetId = data[offset + 4];
                    string json = Encoding.UTF8.GetString(data, offset + 5, length - 1);
                    offset += 4 + length;

                    ProcessPacket(packetId, json);
                }

                if (offset > 0)
                {
                    byte[] remainder = new byte[data.Length - offset];
                    Array.Copy(data, offset, remainder, 0, remainder.Length);
                    m_IncomingBuffer = new MemoryStream();
                    m_IncomingBuffer.Write(remainder, 0, remainder.Length);
                }
            }

            private void ProcessPacket(byte packetId, string json)
            {
                try
                {
                    if (packetId == 0x01) // AuthRequest
                    {
                        string username = GetJsonValue(json, "username");
                        string password = GetJsonValue(json, "password");

                        Account acct = Accounts.GetAccount(username) as Account;
                        if (acct != null && acct.CheckPassword(password) && acct.AccessLevel >= AccessLevel.GameMaster)
                        {
                            m_IsAuthenticated = true;
                            m_Account = acct;

                            var response = new { success = true, gmName = username };
                            SendPacket(0x81, SerializeJson(response));
                            Console.WriteLine(string.Format("[GumpForgeLink] GM authenticated successfully: '{0}'", username));
                        }
                        else
                        {
                            var response = new { success = false, errorMessage = "Invalid credentials or insufficient staff level." };
                            SendPacket(0x81, SerializeJson(response));
                            Disconnect();
                        }
                    }
                    else if (packetId == 0x02) // RequestTarget
                    {
                        if (m_IsAuthenticated)
                        {
                            Mobile gmChar = null;
                            for (int i = 0; i < m_Account.Length; i++)
                            {
                                Mobile mob = m_Account[i];
                                if (mob != null && mob.NetState != null)
                                {
                                    gmChar = mob;
                                    break;
                                }
                            }

                            if (gmChar != null)
                            {
                                gmChar.Target = new GumpForgeTarget(this);
                                gmChar.SendMessage(0x5a, "Target a player to load their context, or target an item to fetch its gump menu.");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(string.Format("[GumpForgeLink] Error processing packet: {0}", e.Message));
                }
            }

            public void SendPacket(byte packetId, string json)
            {
                try
                {
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length + 1));

                    m_Stream.Write(lengthPrefix, 0, 4);
                    m_Stream.WriteByte(packetId);
                    m_Stream.Write(payload, 0, payload.Length);
                    m_Stream.Flush();
                }
                catch { }
            }

            private void Disconnect()
            {
                try { m_Client.Close(); } catch { }
                lock (m_Connections)
                {
                    m_Connections.Remove(this);
                }
            }
        }
    }
}
