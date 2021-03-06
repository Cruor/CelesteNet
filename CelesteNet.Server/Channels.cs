﻿using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class Channels : IDisposable {

        public const string NameDefault = "main";
        public const string NamePrivate = "!<private>";
        public const string PrefixPrivate = "!";

        public readonly CelesteNetServer Server;

        public readonly Channel Default;
        public readonly List<Channel> All = new List<Channel>();
        public readonly Dictionary<uint, Channel> ByID = new Dictionary<uint, Channel>();
        public readonly Dictionary<CelesteNetPlayerSession, Channel> BySession = new Dictionary<CelesteNetPlayerSession, Channel>();
        public readonly Dictionary<string, Channel> ByName = new Dictionary<string, Channel>();
        public uint NextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

        public Channels(CelesteNetServer server) {
            Server = server;

            Server.Data.RegisterHandlersIn(this);

            Default = new Channel(this, NameDefault, 0);

            Server.OnSessionStart += OnSessionStart;
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "channels", "Startup");
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "channels", "Shutdown");
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            if (Server.UserData.TryLoad(session.UID, out LastChannelUserInfo last) &&
                last.Name != NameDefault) {
                Move(session, last.Name);
            } else {
                Default.Add(session);
                BroadcastList();
            }
        }

        public void SendListTo(CelesteNetPlayerSession session) {
            if (!BySession.TryGetValue(session, out Channel? own))
                own = Default;

            lock (All)
                session.Con.Send(new DataChannelList {
                    List = All.Where(c => !c.IsPrivate || c == own).Select(c => new DataChannelList.Channel {
                        Name = c.Name,
                        ID = c.ID,
                        Players = c.Players.Select(p => p.ID).ToArray()
                    }).ToArray()
                });
        }

        public Action<Channels>? OnBroadcastList;

        public void BroadcastList() {
            lock (All)
                foreach (Channel c in All.ToArray())
                    c.RemoveStale();

            OnBroadcastList?.Invoke(this);

            lock (All)
                using (Server.ConLock.R())
                    foreach (CelesteNetPlayerSession session in Server.Sessions)
                        SendListTo(session);
        }

        public Channel Get(CelesteNetPlayerSession session) {
            if (BySession.TryGetValue(session, out Channel? c))
                return c;

            return Default;
        }

        public Tuple<Channel, Channel> Move(CelesteNetPlayerSession session, string name) {
            name = name.Sanitize();
            if (name.Length > Server.Settings.MaxChannelNameLength)
                name = name.Substring(0, Server.Settings.MaxChannelNameLength);
            if (name == NamePrivate)
                throw new Exception("Invalid private channel name.");

            lock (All) {
                Channel prev = Get(session);

                Channel c;

                if (ByName.TryGetValue(name, out Channel? existing)) {
                    c = existing;
                    if (prev == c)
                        return Tuple.Create(c, c);

                } else {
                    c = new Channel(this, name, NextID++);
                }

                prev.Remove(session);

                if (session.PlayerInfo != null)
                    c.Add(session);

                DataInternalBlob move = new DataInternalBlob(Server.Data, new DataChannelMove {
                    Player = session.PlayerInfo
                });
                session.Con.Send(move);
                foreach (CelesteNetPlayerSession other in prev.Players)
                    other.Con.Send(move);

                BroadcastList();

                session.ResendPlayerStates();

                if (!Server.UserData.GetKey(session.UID).IsNullOrEmpty()) {
                    Server.UserData.Save(session.UID, new LastChannelUserInfo {
                        Name = name
                    });
                }

                return Tuple.Create(prev, c);
            }
        }

    }

    public class Channel {
        public readonly Channels Ctx;
        public readonly string Name;
        public readonly uint ID;
        public readonly HashSet<CelesteNetPlayerSession> Players = new HashSet<CelesteNetPlayerSession>();

        public readonly bool IsPrivate;
        public readonly string PublicName;

        public Channel(Channels ctx, string name, uint id) {
            Ctx = ctx;
            Name = name;
            ID = id;

            if (IsPrivate = name.StartsWith(Channels.PrefixPrivate)) {
                PublicName = Channels.NamePrivate;

            } else {
                PublicName = name;
            }

            lock (Ctx.All) {
                Ctx.All.Add(this);
                Ctx.ByName[Name] = this;
                Ctx.ByID[ID] = this;
            }
        }

        public void RemoveStale() {
            lock (Players) {
                HashSet<CelesteNetPlayerSession> stale = new HashSet<CelesteNetPlayerSession>();
                foreach (CelesteNetPlayerSession session in Players)
                    if (session.PlayerInfo == null)
                        stale.Add(session);
                foreach (CelesteNetPlayerSession session in stale)
                    Remove(session);
            }
        }

        public void Add(CelesteNetPlayerSession session) {
            lock (Players)
                if (!Players.Add(session))
                    return;

            Ctx.BySession[session] = this;

            session.OnEnd += RemoveByDC;

            if (session.PlayerInfo == null)
                Remove(session);
        }

        public void Remove(CelesteNetPlayerSession session) {
            lock (Players)
                if (!Players.Remove(session))
                    return;

            Ctx.BySession.Remove(session);
            session.OnEnd -= RemoveByDC;

            if (ID == 0)
                return;

            lock (Ctx.All) {
                if (Players.Count > 0)
                    return;

                Ctx.All.Remove(this);
                Ctx.ByName.Remove(Name);
                Ctx.ByID.Remove(ID);
            }
        }

        private void RemoveByDC(CelesteNetPlayerSession session, DataPlayerInfo? lastInfo) {
            Remove(session);
            Ctx.BroadcastList();
        }
    }

    public class LastChannelUserInfo {
        public string Name = "main";
    }
}
