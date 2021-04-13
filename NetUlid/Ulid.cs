﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright(c) 2021 Ultima Microsystems
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
namespace NetUlid
{
    using System;
    using System.Buffers.Binary;
    using System.ComponentModel;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;

    /// <summary>
    /// Represents a Universally Unique Lexicographically Sortable Identifier (ULID).
    /// </summary>
    /// <remarks>
    /// This is an implementation of https://github.com/ulid/spec.
    /// </remarks>
    [TypeConverter(typeof(UlidConverter))]
    public unsafe struct Ulid : IComparable, IComparable<Ulid>, IEquatable<Ulid>
    {
        /// <summary>
        /// Represents the largest possible value of timestamp part.
        /// </summary>
        public const long MaxTimestamp = 0xFFFFFFFFFFFF;

        /// <summary>
        /// Represents the smallest possible value of timestamp part.
        /// </summary>
        public const long MinTimestamp = 0;

        /// <summary>
        /// A read-only instance of <see cref="Ulid"/> whose value is all zeros.
        /// </summary>
        public static readonly Ulid Null = default;

        private const string Base32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int DataSize = 16;
        private fixed byte data[DataSize];

        /// <summary>
        /// Initializes a new instance of the <see cref="Ulid"/> structure by using the specified timestamp and randomness.
        /// </summary>
        /// <param name="timestamp">
        /// The milliseconds since January 1, 1970 12:00 AM UTC.
        /// </param>
        /// <param name="randomness">
        /// The 80-bits cryptographically randomness.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timestamp"/> is lower than <see cref="MinTimestamp"/> or greater than <see cref="MaxTimestamp"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="randomness"/> is not 10 bytes exactly.
        /// </exception>
        public Ulid(long timestamp, ReadOnlySpan<byte> randomness)
        {
            // Sanity checks.
            if (timestamp < MinTimestamp || timestamp > MaxTimestamp)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            if (randomness.Length != 10)
            {
                throw new ArgumentException("The value must be 10 bytes exactly.", nameof(randomness));
            }

            fixed (void* d = this.data)
            {
                fixed (void* s = randomness.ToArray().Reverse().Concat(BitConverter.GetBytes(timestamp)).ToArray())
                {
                    Unsafe.CopyBlockUnaligned(d, s, DataSize);
                }
            }

            fixed (void* nd = Null.data)
            {
                fixed (void* d = this.data)
                {
                    fixed (void* s = this.ToByteArray().Reverse().ToArray())
                    {
                        Unsafe.CopyBlockUnaligned(d, s, DataSize);
                        Unsafe.CopyBlockUnaligned(nd, s, DataSize);
                    }
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ulid"/> struct from the specified binary representation.
        /// </summary>
        /// <param name="binary">
        /// A <see cref="ReadOnlySpan{T}"/> containing binary representation.
        /// </param>
        /// <exception cref="ArgumentException">
        /// <paramref name="binary"/> is not 16 bytes exactly.
        /// </exception>
        public Ulid(ReadOnlySpan<byte> binary)
        {
            if (binary.Length != DataSize)
            {
                throw new ArgumentException($"The value must be {DataSize} bytes exactly.", nameof(binary));
            }

            fixed (void* d = this.data)
            {
                fixed (void* s = binary)
                {
                    Unsafe.CopyBlockUnaligned(d, s, DataSize);
                }
            }
        }

        public static bool operator ==(Ulid left, Ulid right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Ulid left, Ulid right)
        {
            return !(left == right);
        }

        public static bool operator <(Ulid left, Ulid right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(Ulid left, Ulid right)
        {
            return left.CompareTo(right) <= 0;
        }

        public static bool operator >(Ulid left, Ulid right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(Ulid left, Ulid right)
        {
            return left.CompareTo(right) >= 0;
        }

        /// <summary>
        /// Create a new <see cref="Ulid"/> with the current time as a timestamp.
        /// </summary>
        /// <returns>
        /// An <see cref="Ulid"/> with the current time as a timestamp and cryptographically randomness.
        /// </returns>
        /// <exception cref="OverflowException">
        /// The generate operation result in the same timestamp as the last generated value and the randomness incrementing is overflow.
        /// </exception>
        public static Ulid Generate() => Generate(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>
        /// Create a new <see cref="Ulid"/> with the specified timestamp.
        /// </summary>
        /// <param name="timestamp">
        /// Timestamp to use.
        /// </param>
        /// <returns>
        /// An <see cref="Ulid"/> with the specified timestamp and cryptographically randomness.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="timestamp"/> is less than <see cref="MinTimestamp"/> or greater than <see cref="MaxTimestamp"/>.
        /// </exception>
        /// <exception cref="OverflowException">
        /// <paramref name="timestamp"/> is the same as the last generated value and the randomness incrementing is overflow.
        /// </exception>
        public static Ulid Generate(long timestamp)
        {
            if (timestamp < MinTimestamp || timestamp > MaxTimestamp)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp));
            }

            var oldTimestamp = Null.ToByteArray().Take(6).Reverse().ToList();
            oldTimestamp.Insert(6, 0);
            oldTimestamp.Insert(7, 0);
            var oldRandomness = Null.ToByteArray().Skip(6).ToArray();
            if (oldTimestamp.SequenceEqual(BitConverter.GetBytes(timestamp)))
            {
                var newLast = Convert.ToInt32(oldRandomness[9]) + 1;
                byte[] bytes = BitConverter.GetBytes(newLast);
                oldRandomness[9] = bytes.First();
                return new Ulid(timestamp, oldRandomness);
            }
            else
            {
                byte[] randomness = new byte[10];
                using (RNGCryptoServiceProvider RNG = new RNGCryptoServiceProvider())
                {
                    RNG.GetBytes(randomness);
                }

                return new Ulid(timestamp, randomness);
            }
        }

        /// <summary>
        /// Create an <see cref="Ulid"/> from canonical representation.
        /// </summary>
        /// <param name="s">
        /// Canonical representation to convert.
        /// </param>
        /// <returns>
        /// An <see cref="Ulid"/> whose value the same as <paramref name="s"/>.
        /// </returns>
        /// <exception cref="FormatException">
        /// <paramref name="s"/> is not a valid canonical representation.
        /// </exception>
        public static Ulid Parse(string s)
        {
            // Sanity check.
            if (s.Length != 26)
            {
                throw new FormatException();
            }

            Ulid ulid = default;
            s = s.ToUpperInvariant();

            int[] index = new int[26];

            bool doIt = true;
            for (int i = 0; i < 26; ++i)
            {
                char c = s[i];
                bool found = false;

                for (int v = 0; v < Base32.Length; ++v)
                {
                    if (Base32[v] == c)
                    {
                        index[i] = v;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    doIt = false;
                    break;
                }
            }

            if (doIt)
            {
                var data = ulid.ToByteArray();
                data[0] = (byte)(index[0] << 5 | index[1]);
                data[1] = (byte)(index[2] << 3 | index[3] >> 2);
                data[2] = (byte)(index[3] << 6 | index[4] << 1 | index[5] >> 4);
                data[3] = (byte)(index[5] << 4 | index[6] >> 1);
                data[4] = (byte)(index[6] << 7 | index[7] << 2 | index[8] >> 3);
                data[5] = (byte)(index[8] << 5 | index[9]);
                data[6] = (byte)(index[10] << 3 | index[11] >> 2);
                data[7] = (byte)(index[11] << 6 | index[12] << 1 | index[13] >> 4);
                data[8] = (byte)(index[13] << 4 | index[14] >> 1);
                data[9] = (byte)(index[14] << 7 | index[15] << 2 | index[16] >> 3);
                data[10] = (byte)(index[16] << 5 | index[17]);
                data[11] = (byte)(index[18] << 3 | index[19] >> 2);
                data[12] = (byte)(index[19] << 6 | index[20] << 1 | index[21] >> 4);
                data[13] = (byte)(index[21] << 4 | index[22] >> 1);
                data[14] = (byte)(index[22] << 7 | index[23] << 2 | index[24] >> 3);
                data[15] = (byte)(index[24] << 5 | index[25]);
                return new Ulid(data);
            }

            return ulid;
        }

        public int CompareTo(Ulid other)
        {
            for (var i = 0; i < DataSize; i++)
            {
                var l = this.data[i];
                var r = other.data[i];

                if (l < r)
                {
                    return -1;
                }
                else if (l > r)
                {
                    return 1;
                }
            }

            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null)
            {
                return 1;
            }
            else if (obj.GetType() != this.GetType())
            {
                throw new ArgumentException($"The value is not an instance of {this.GetType()}.", nameof(obj));
            }

            return this.CompareTo((Ulid)obj);
        }

        public bool Equals(Ulid other)
        {
            for (var i = 0; i < DataSize; i++)
            {
                if (this.data[i] != other.data[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || obj.GetType() != this.GetType())
            {
                return false;
            }

            return this.Equals((Ulid)obj);
        }

        public override int GetHashCode()
        {
            // https://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-overriding-gethashcode
            var result = unchecked((int)2166136261);

            for (var i = 0; i < DataSize; i++)
            {
                result = (result * 16777619) ^ this.data[i];
            }

            return result;
        }

        /// <summary>
        /// Copy binary representation of this <see cref="Ulid"/> to the specified <see cref="Span{T}"/>.
        /// </summary>
        /// <param name="output">
        /// The <see cref="Span{T}"/> to receive binary representation.
        /// </param>
        /// <exception cref="ArgumentException">
        /// Not enough space in <paramref name="output"/>.
        /// </exception>
        public void Write(Span<byte> output)
        {
            if (output.Length < DataSize)
            {
                throw new ArgumentException("The size of buffer is not enough.", nameof(output));
            }

            fixed (void* d = output)
            {
                fixed (void* s = this.data)
                {
                    Unsafe.CopyBlockUnaligned(d, s, DataSize);
                }
            }
        }

        /// <summary>
        /// Converts the current value to binary representation.
        /// </summary>
        /// <returns>
        /// The binary representation of this value.
        /// </returns>
        public byte[] ToByteArray()
        {
            var result = new byte[DataSize];

            fixed (void* d = result)
            {
                fixed (void* s = this.data)
                {
                    Unsafe.CopyBlockUnaligned(d, s, DataSize);
                }
            }

            return result;
        }

        public override string ToString()
        {
            Span<char> result = stackalloc char[26];

            result[0] = Base32[this.data[0] >> 5];
            result[1] = Base32[this.data[0] & 0x1F];
            result[2] = Base32[this.data[1] >> 3];
            result[3] = Base32[((this.data[1] & 0x7) << 2) | (this.data[2] >> 6)];
            result[4] = Base32[(this.data[2] >> 1) & 0x1F];
            result[5] = Base32[((this.data[2] & 0x1) << 4) | (this.data[3] >> 4)];
            result[6] = Base32[((this.data[3] & 0xF) << 1) | (this.data[4] >> 7)];
            result[7] = Base32[(this.data[4] >> 2) & 0x1F];
            result[8] = Base32[((this.data[4] & 0x3) << 3) | (this.data[5] >> 5)];
            result[9] = Base32[this.data[5] & 0x1F];
            result[10] = Base32[(this.data[6] >> 3) & 0x1F];
            result[11] = Base32[((this.data[6] & 0x7) << 2) | (this.data[7] >> 6)];
            result[12] = Base32[(this.data[7] >> 1) & 0x1F];
            result[13] = Base32[((this.data[7] & 0x1) << 4) | (this.data[8] >> 4)];
            result[14] = Base32[((this.data[8] & 0xF) << 1) | (this.data[9] >> 7)];
            result[15] = Base32[(this.data[9] >> 2) & 0x1F];
            result[16] = Base32[((this.data[9] & 0x3) << 3) | (this.data[10] >> 5)];
            result[17] = Base32[this.data[10] & 0x1F];
            result[18] = Base32[(this.data[11] >> 3) & 0x1F];
            result[19] = Base32[((this.data[11] & 0x7) << 2) | (this.data[12] >> 6)];
            result[20] = Base32[(this.data[12] >> 1) & 0x1F];
            result[21] = Base32[((this.data[12] & 0x1) << 4) | (this.data[13] >> 4)];
            result[22] = Base32[((this.data[13] & 0xF) << 1) | (this.data[14] >> 7)];
            result[23] = Base32[(this.data[14] >> 2) & 0x1F];
            result[24] = Base32[((this.data[14] & 0x3) << 3) | (this.data[15] >> 5)];
            result[25] = Base32[this.data[15] & 0x1F];

            return new string(result);
        }
    }
}
