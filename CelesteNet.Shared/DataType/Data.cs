﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public abstract class DataType {

        public virtual DataFlags DataFlags => DataFlags.None;

        public virtual MetaType[] Meta { get; set; } = Dummy<MetaType>.EmptyArray;

        public virtual bool FilterHandle(DataContext ctx) => true;
        public virtual bool FilterSend(DataContext ctx) => true;

        public virtual bool ConsideredDuplicate(DataType data) => false;

        public virtual MetaType[] GenerateMeta(DataContext ctx)
            => Meta;

        public virtual void FixupMeta(DataContext ctx) {
        }

        public virtual MetaUpdateContext UpdateMeta(DataContext ctx)
            => new MetaUpdateContext(ctx, this);

        public virtual void ReadAll(DataContext ctx, BinaryReader reader) {
            UnwrapMeta(ctx, ctx.ReadMeta(reader));
            Read(ctx, reader);
        }

        public virtual void WriteAll(DataContext ctx, BinaryWriter writer) {
            ctx.WriteMeta(writer, WrapMeta(ctx));
            Write(ctx, writer);
        }

        public abstract void Read(DataContext ctx, BinaryReader reader);
        public abstract void Write(DataContext ctx, BinaryWriter writer);

        public virtual bool Is<T>(DataContext ctx) where T : MetaType<T> {
            foreach (MetaType meta in Meta)
                if (meta is T)
                    return true;
            return false;
        }

        public virtual T Get<T>(DataContext ctx) where T : MetaType<T>
            => TryGet(ctx, out T? value) ? value : throw new Exception($"DataType {ctx.DataTypeToID[GetType()]} doesn't have MetaType {ctx.MetaTypeToID[typeof(T)]}.");

        public virtual T? GetOpt<T>(DataContext ctx) where T : MetaType<T>
            => TryGet(ctx, out T? value) ? value : null;

        public virtual bool TryGet<T>(DataContext ctx, [NotNullWhen(true)] out T? value) where T : MetaType<T> {
            foreach (MetaType meta in Meta)
                if (meta is T cast) {
                    value = cast;
                    return true;
                }
            value = null;
            return false;
        }

        public virtual void Set<T>(DataContext ctx, T? value) where T : MetaType<T> {
            if (value == null) {
                Meta = Meta.Where(m => !(m is T)).ToArray();
                return;
            }

            for (int i = 0; i < Meta.Length; i++) {
                MetaType meta = Meta[i];
                if (meta == value || meta is T) {
                    Meta[i] = value;
                    return;
                }
            }

            Meta = Meta.Concat(new MetaType[] { value }).ToArray();
        }

        public virtual MetaTypeWrap[] WrapMeta(DataContext ctx) {
            MetaType[] metas = Meta;
            MetaTypeWrap[] wraps = new MetaTypeWrap[metas.Length];
            for (int i = 0; i < metas.Length; i++)
                wraps[i] = new MetaTypeWrap().Wrap(ctx, metas[i]);
            return wraps;
        }

        public virtual void UnwrapMeta(DataContext ctx, MetaTypeWrap[] wraps) {
            MetaType[] metas = new MetaType[wraps.Length];
            for (int i = 0; i < wraps.Length; i++)
                metas[i] = wraps[i].Unwrap(ctx);
            Meta = metas;
            FixupMeta(ctx);
        }

        public virtual string GetTypeID(DataContext ctx)
            => ctx.DataTypeToID.TryGetValue(GetType(), out string? value) ? value : "";

        public virtual string GetSource(DataContext ctx)
            => ctx.DataTypeToSource.TryGetValue(GetType(), out string? value) ? value : "";

        public static byte PackBool(byte value, int index, bool set) {
            int mask = 1 << index;
            return set ? (byte) (value | mask) : (byte) (value & ~mask);
        }

        public static bool UnpackBool(byte value, int index) {
            int mask = 1 << index;
            return (value & mask) == mask;
        }

        public static byte PackBools(bool a = false, bool b = false, bool c = false, bool d = false, bool e = false, bool f = false, bool g = false, bool h = false) {
            byte value = 0;
            value = PackBool(value, 0, a);
            value = PackBool(value, 1, b);
            value = PackBool(value, 2, c);
            value = PackBool(value, 3, d);
            value = PackBool(value, 4, e);
            value = PackBool(value, 5, f);
            value = PackBool(value, 6, g);
            value = PackBool(value, 7, h);
            return value;
        }

    }

    public abstract class DataType<T> : DataType where T : DataType<T> {

        public static string DataID;
        public static string DataSource;

        static DataType() {
            DataID = typeof(T).Name;
            DataSource = typeof(T).Assembly.GetName().Name ?? DataID;
        }

        public T ReadT(DataContext ctx, BinaryReader reader) {
            Read(ctx, reader);
            return (T) this;
        }

        public T ReadAllT(DataContext ctx, BinaryReader reader) {
            ReadAll(ctx, reader);
            return (T) this;
        }

        public override string GetTypeID(DataContext ctx)
            => DataID;

        public override string GetSource(DataContext ctx)
            => DataSource;

    }

    public class MetaUpdateContext : IDisposable {

        public readonly DataContext Context;
        public readonly DataType Data;

        public MetaUpdateContext(DataContext ctx, DataType data) {
            Context = ctx;
            Data = data;
            Data.Meta = data.GenerateMeta(Context);
        }

        public void Dispose() {
            Data.FixupMeta(Context);
        }

    }

    // Used for compile time verification and to make getting the request type easier to obtain.
    public interface IDataRequestable {
    }
    public interface IDataRequestable<T> : IDataRequestable where T : DataType<T>, new() {
    }

    [Flags]
    public enum DataFlags : ushort {
        None =
            0b0000000000000000,
        Update =
            0b0000000000000001,
        ForceForward =
            0b0000000000000010,
        OnlyLatest =
            0b0000000000000100,
        SkipDuplicate =
            0b0000000000001000,

        Reserved3 =
            0b0000010000000000,
        Reserved2 =
            0b0000100000000000,

        Taskable =
            0b0001000000000000,

        Small =
            0b0010000000000000,
        Big =
            0b0100000000000000,

        Reserved1 =
            0b1000000000000000
    }
}
