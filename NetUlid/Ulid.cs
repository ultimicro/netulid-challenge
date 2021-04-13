////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
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
    using System.Numerics;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;

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
        private static long LatestTimeStamp = 0;
        private static long LatestRandomness = 0;

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
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            Array.Reverse(timestampBytes);
            timestampBytes = new ArraySegment<byte>(timestampBytes, 2, 6).ToArray();

            byte[] finalResult = new byte[timestampBytes.Length + randomness.Length];
            timestampBytes.CopyTo(finalResult, 0);
            randomness.ToArray().CopyTo(finalResult, timestampBytes.Length);

            fixed (void* d = this.data)
            {
                fixed (void* s = finalResult)
                {
                    Unsafe.CopyBlockUnaligned(d, s, DataSize);
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

            //https://github.com/ulid/spec#monotonicity
            //When generating a ULID within the same millisecond, we can provide some guarantees regarding sort order.
            //Namely, if the same millisecond is detected, the random component is incremented by 1 bit in the least
            //significant bit position (with carrying)

            if (timestamp == LatestTimeStamp)
            {
                LatestRandomness++;
            }
            else
            {
                LatestTimeStamp = timestamp;
                LatestRandomness = RandomLong(0, long.MaxValue);
            }
            
            byte[] timestampBytes = BitConverter.GetBytes(LatestTimeStamp);
            Array.Reverse(timestampBytes);
            timestampBytes = new ArraySegment<byte>(timestampBytes, 2, 6).ToArray();

            //find randomness
            byte[] randomness = BitConverter.GetBytes(LatestRandomness);
            Array.Reverse(randomness);
            byte[] finalRandomness = new byte[10];
            randomness.CopyTo(finalRandomness, 2);

            byte[] finalId = new byte[finalRandomness.Length + timestampBytes.Length];

            timestampBytes.CopyTo(finalId, 0);
            finalRandomness.CopyTo(finalId, timestampBytes.Length);

            return new Ulid(finalId);
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

            //https://github.com/ulid/spec#encoding
            //Encoding
            //Crockford's Base32 is used as shown. This alphabet excludes the letters I, L, O, and U to avoid confusion and abuse.
            //https://github.com/atifaziz/Crockbase32

            string timeStamp = s.Substring(0, 10).PadLeft(16, '0');//Pad it to be 2 byte
            string randomness = s.Substring(10, 16);

            byte[] decoded = Crockbase32.Decode(timeStamp);
            byte[] timeStampBytes = new ArraySegment<byte>(decoded, 4, 6).ToArray();
            byte[] randomnessBytes = Crockbase32.Decode(randomness);
            byte[] finalBytes = new byte[timeStampBytes.Length  + randomnessBytes.Length];

            timeStampBytes.CopyTo(finalBytes, 0);
            randomnessBytes.CopyTo(finalBytes, timeStampBytes.Length );
            return new Ulid(finalBytes);
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

        /// <summary>
        /// Random number as long
        /// </summary>
        //https://stackoverflow.com/questions/6651554/random-number-in-long-range-is-this-the-way
        public static long RandomLong(long min, long max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException("max", "max must be > min!");

            //Working with ulong so that modulo works correctly with values > long.MaxValue
            ulong uRange = (ulong)(max - min);

            //Prevent a modolo bias; see https://stackoverflow.com/a/10984975/238419
            //for more information.
            //In the worst case, the expected number of calls is 2 (though usually it's
            //much closer to 1) so this loop doesn't really hurt performance at all.
            ulong ulongRand;
            Random random = new Random();
            do
            {
                byte[] buf = new byte[8];
                random.NextBytes(buf);
                ulongRand = (ulong)BitConverter.ToInt64(buf, 0);
            } while (ulongRand > ulong.MaxValue - ((ulong.MaxValue % uRange) + 1) % uRange);

            return (long)(ulongRand % uRange) + min;
        }
    }
}