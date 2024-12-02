// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Garnet.common;
using Tsavorite.core;

namespace Garnet.server
{
    using MainStoreAllocator = SpanByteAllocator<StoreFunctions<SpanByte, SpanByte, SpanByteComparer, SpanByteRecordDisposer>>;
    using MainStoreFunctions = StoreFunctions<SpanByte, SpanByte, SpanByteComparer, SpanByteRecordDisposer>;

    using ObjectStoreAllocator = GenericAllocator<byte[], IGarnetObject, StoreFunctions<byte[], IGarnetObject, ByteArrayKeyComparer, DefaultRecordDisposer<byte[], IGarnetObject>>>;
    using ObjectStoreFunctions = StoreFunctions<byte[], IGarnetObject, ByteArrayKeyComparer, DefaultRecordDisposer<byte[], IGarnetObject>>;

    sealed partial class StorageSession : IDisposable
    {
        private const byte DUMP_VERSION = 9;
        private const byte DUMP_STRING_TYPE = 0;
        private const byte DUMP_LIST_TYPE = 1;
        private const byte DUMP_SET_TYPE = 2;
        private const byte DUMP_SORTED_SET_TYPE = 3;
        private const byte DUMP_HASH_TYPE = 4;
        private const byte DUMP_END_MARKER = 0xFF;

        /// <summary>
        /// Serializes a key's value into Redis DUMP format.
        /// Format: [version][type][content][0xFF][8-byte-checksum]
        /// </summary>
        public GarnetStatus DUMP<TContext, TObjectContext>(ArgSlice key, out byte[] output, ref TContext context, ref TObjectContext objectContext)
            where TContext : ITsavoriteContext<SpanByte, SpanByte, RawStringInput, SpanByteAndMemory, long, MainSessionFunctions, MainStoreFunctions, MainStoreAllocator>
            where TObjectContext : ITsavoriteContext<byte[], IGarnetObject, ObjectInput, GarnetObjectStoreOutput, long, ObjectSessionFunctions, ObjectStoreFunctions, ObjectStoreAllocator>
        {
            output = default;
            // First check main store
            var status = GET(key, out ArgSlice value, ref context);
            if (status == GarnetStatus.OK)
            {
                return DumpString(value, out output);
            }

            // Then check object store
            if (!objectStoreBasicContext.IsNull)
            {
                status = GET(key.ToArray(), out GarnetObjectStoreOutput objOutput, ref objectContext);
                if (status == GarnetStatus.OK)
                {
                    if (objOutput.garnetObject is ListObject listObj)
                        return DumpList(listObj, out output);
                    else if (objOutput.garnetObject is SetObject setObj)
                        return DumpSet(setObj, out output);
                    else if (objOutput.garnetObject is SortedSetObject sortedSetObj)
                        return DumpSortedSet(sortedSetObj, out output);
                    else if (objOutput.garnetObject is HashObject hashObj)
                        return DumpHash(hashObj, out output);
                }
            }

            return GarnetStatus.NOTFOUND;
        }

        private GarnetStatus DumpHash(HashObject hashObj, out byte[] output) => throw new NotImplementedException();
        private GarnetStatus DumpSortedSet(SortedSetObject sortedSetObj, out byte[] output) => throw new NotImplementedException();
        private GarnetStatus DumpSet(SetObject setObj, out byte[] output) => throw new NotImplementedException();

        /// <summary>
        /// Creates DUMP format for string values
        /// </summary>
        private GarnetStatus DumpString(ArgSlice value, out byte[] output)
        {
            // Format: [version][type][encoded-string][0xFF][8-byte-checksum]
            var length = 1 + 1 + value.Length + 1 + 8;
            var dumpBytes = new byte[length];
            var span = dumpBytes.AsSpan();

            var pos = 0;
            span[pos++] = DUMP_VERSION;
            span[pos++] = DUMP_STRING_TYPE;
            
            value.Span.CopyTo(span[pos..]);
            pos += value.Length;
            
            span[pos++] = DUMP_END_MARKER;
            
            var checksum = CalculateCrc64(span[..pos]);
            BitConverter.TryWriteBytes(span[pos..], checksum);

            output = dumpBytes;
            return GarnetStatus.OK;
        }

        /// <summary>
        /// Creates DUMP format for list values
        /// </summary>
        private GarnetStatus DumpList(ListObject list, out byte[] output)
        {
            // Calculate total size needed
            var totalSize = 1 + 1; // Version + Type
            var items = list.LnkList;
            foreach (var item in items)
            {
                totalSize += item.Length;
            }
            totalSize += 1 + 8; // End marker + CRC64

            var dumpBytes = new byte[totalSize];
            var span = dumpBytes.AsSpan();

            var pos = 0;
            span[pos++] = DUMP_VERSION;
            span[pos++] = DUMP_LIST_TYPE;

            foreach (var item in items)
            {
                item.AsSpan().CopyTo(span[pos..]);
                pos += item.Length;
            }

            span[pos++] = DUMP_END_MARKER;
            
            var checksum = CalculateCrc64(span[..pos]); 
            BitConverter.TryWriteBytes(span[pos..], checksum);

            output = dumpBytes;
            return GarnetStatus.OK;
        }

        // Similar implementations for Set, SortedSet and Hash...

        private ulong CalculateCrc64(ReadOnlySpan<byte> data)
        {
            // Redis uses CRC64 with ECMA polynomial
            ulong crc = 0;
            foreach (var b in data)
            {
                crc = ((crc << 8) | b) ^ Crc64Table[(crc >> 56) & 0xFF];
            }
            return crc;
        }
        
        // CRC64 lookup table using ECMA polynomial
        private static readonly ulong[] Crc64Table = GenerateCrc64Table();

        private static ulong[] GenerateCrc64Table()
        {
            const ulong ECMA_POLY = 0xC96C5795D7870F42;
            var table = new ulong[256];
            for (var i = 0; i < 256; i++)
            {
                ulong crc = (ulong)i;
                for (var j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ ECMA_POLY;
                    else
                        crc >>= 1;
                }
                table[i] = crc;
            }
            return table;
        }
    }
}