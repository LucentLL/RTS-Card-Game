using System;

namespace SpawnRowDuel.Net
{
    /// <summary>
    /// URL-safe base64 without padding.
    ///
    /// Frames are binary but the relay carries a text message body, so every frame is base64 on
    /// the way out. Url-safe rather than standard because the same encoding is used in places
    /// that end up in a URL, and one alphabet everywhere is one fewer thing to get wrong.
    ///
    /// Hand-written for the same reason as everything else in this folder: Convert.ToBase64String
    /// would do, but its url-safe variant does not exist and the padding fixups are exactly the
    /// kind of thing that differs by a byte between platforms.
    /// </summary>
    public static class Base64Url
    {
        const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            var sb = new System.Text.StringBuilder((data.Length + 2) / 3 * 4);

            int i = 0;
            while (i + 3 <= data.Length)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63])
                  .Append(Alphabet[(v >> 6) & 63]).Append(Alphabet[v & 63]);
                i += 3;
            }

            int rem = data.Length - i;
            if (rem == 1)
            {
                int v = data[i] << 16;
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63]);
            }
            else if (rem == 2)
            {
                int v = (data[i] << 16) | (data[i + 1] << 8);
                sb.Append(Alphabet[(v >> 18) & 63]).Append(Alphabet[(v >> 12) & 63])
                  .Append(Alphabet[(v >> 6) & 63]);
            }
            return sb.ToString();
        }

        static int Value(char c)
        {
            if (c >= 'A' && c <= 'Z') return c - 'A';
            if (c >= 'a' && c <= 'z') return c - 'a' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            if (c == '-' || c == '+') return 62;
            if (c == '_' || c == '/') return 63;
            return -1;
        }

        /// <summary>Null on anything malformed. Callers treat that exactly like a failed auth
        /// tag: the relay topic is public, so garbage on it is expected, not exceptional.</summary>
        public static byte[] Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            int n = 0;
            var vals = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '=' || char.IsWhiteSpace(c)) continue;
                int v = Value(c);
                if (v < 0) return null;
                vals[n++] = v;
            }
            if (n % 4 == 1) return null;

            int outLen = n / 4 * 3 + (n % 4 == 2 ? 1 : n % 4 == 3 ? 2 : 0);
            var outp = new byte[outLen];

            int oi = 0, i2 = 0;
            while (n - i2 >= 4)
            {
                int v = (vals[i2] << 18) | (vals[i2 + 1] << 12) | (vals[i2 + 2] << 6) | vals[i2 + 3];
                outp[oi++] = (byte)(v >> 16); outp[oi++] = (byte)(v >> 8); outp[oi++] = (byte)v;
                i2 += 4;
            }
            int rem = n - i2;
            if (rem == 2)
            {
                int v = (vals[i2] << 18) | (vals[i2 + 1] << 12);
                outp[oi++] = (byte)(v >> 16);
            }
            else if (rem == 3)
            {
                int v = (vals[i2] << 18) | (vals[i2 + 1] << 12) | (vals[i2 + 2] << 6);
                outp[oi++] = (byte)(v >> 16); outp[oi++] = (byte)(v >> 8);
            }
            return outp;
        }
    }
}
