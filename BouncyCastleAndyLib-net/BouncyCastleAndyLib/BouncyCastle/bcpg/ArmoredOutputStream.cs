using System;
using System.Collections;
using System.IO;

using Org.BouncyCastle.Utilities.IO;

namespace Org.BouncyCastle.Bcpg
{
    /**
    * Basic output stream.
    */
    public class ArmoredOutputStream
        : BaseOutputStream
    {
        private static readonly byte[] encodingTable =
        {
            (byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E', (byte)'F', (byte)'G',
            (byte)'H', (byte)'I', (byte)'J', (byte)'K', (byte)'L', (byte)'M', (byte)'N',
            (byte)'O', (byte)'P', (byte)'Q', (byte)'R', (byte)'S', (byte)'T', (byte)'U',
            (byte)'V', (byte)'W', (byte)'X', (byte)'Y', (byte)'Z',
            (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f', (byte)'g',
            (byte)'h', (byte)'i', (byte)'j', (byte)'k', (byte)'l', (byte)'m', (byte)'n',
            (byte)'o', (byte)'p', (byte)'q', (byte)'r', (byte)'s', (byte)'t', (byte)'u',
            (byte)'v',
            (byte)'w', (byte)'x', (byte)'y', (byte)'z',
            (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6',
            (byte)'7', (byte)'8', (byte)'9',
            (byte)'+', (byte)'/'
        };

        /**
         * encode the input data producing a base 64 encoded byte array.
         */
        private void Encode(
            Stream    outStream,
            int[]     data,
            int       len)
        {
            int    d1, d2, d3;

            switch (len)
            {
            case 0:        /* nothing left to do */
                break;
            case 1:
                d1 = data[0];

                outStream.WriteByte(encodingTable[(d1 >> 2) & 0x3f]);
                outStream.WriteByte(encodingTable[(d1 << 4) & 0x3f]);
                outStream.WriteByte((byte)'=');
                outStream.WriteByte((byte)'=');
                break;
            case 2:
                d1 = data[0];
                d2 = data[1];

                outStream.WriteByte(encodingTable[(d1 >> 2) & 0x3f]);
                outStream.WriteByte(encodingTable[((d1 << 4) | (d2 >> 4)) & 0x3f]);
                outStream.WriteByte(encodingTable[(d2 << 2) & 0x3f]);
                outStream.WriteByte((byte)'=');
                break;
            case 3:
                d1 = data[0];
                d2 = data[1];
                d3 = data[2];

                outStream.WriteByte(encodingTable[(d1 >> 2) & 0x3f]);
                outStream.WriteByte(encodingTable[((d1 << 4) | (d2 >> 4)) & 0x3f]);
                outStream.WriteByte(encodingTable[((d2 << 2) | (d3 >> 6)) & 0x3f]);
                outStream.WriteByte(encodingTable[d3 & 0x3f]);
                break;
            default:
                throw new IOException("unknown length in encode");
            }
        }

        private readonly Stream outStream;
        private int[]           buf = new int[3];
        private int             bufPtr = 0;
        private Crc24           crc = new Crc24();
        private int             chunkCount = 0;
        private int             lastb;

        private bool            start = true;
        private bool            clearText = false;
        private bool            newLine = false;

        private string          nl = System.Environment.NewLine;

        private string          type;
        private string          headerStart = "-----BEGIN PGP ";
        private string          headerTail = "-----";
        private string          footerStart = "-----END PGP ";
        private string          footerTail = "-----";

        private string          version = "BCPG v1.32";

        private readonly IDictionary headers;

        public ArmoredOutputStream(Stream outStream)
        {
            this.outStream = outStream;
            this.headers = new Hashtable();
            this.headers["Version"] = version;
        }

        public ArmoredOutputStream(Stream outStream, IDictionary headers)
        {
            this.outStream = outStream;
            this.headers = new Hashtable(headers);
            this.headers["Version"] = version;
        }

        /**
         * Set an additional header entry.
         *
         * @param name the name of the header entry.
         * @param v the value of the header entry.
         */
        public void SetHeader(
            string name,
            string v)
        {
            headers[name] = v;
        }

        /**
         * Reset the headers to only contain a Version string.
         */
        public void ResetHeaders()
        {
            headers.Clear();
            headers["Version"] = version;
        }

        /**
         * Start a clear text signed message.
         * @param hashAlgorithm
         */
        public void BeginClearText(
            HashAlgorithmTag    hashAlgorithm)
        {
            string    hash;

            switch (hashAlgorithm)
            {
            case HashAlgorithmTag.Sha1:
                hash = "SHA1";
                break;
            case HashAlgorithmTag.Sha256:
                hash = "SHA256";
                break;
            case HashAlgorithmTag.Sha384:
                hash = "SHA384";
                break;
            case HashAlgorithmTag.Sha512:
                hash = "SHA512";
                break;
            case HashAlgorithmTag.MD2:
                hash = "MD2";
                break;
            case HashAlgorithmTag.MD5:
                hash = "MD5";
                break;
            case HashAlgorithmTag.RipeMD160:
                hash = "RIPEMD160";
                break;
            default:
                throw new IOException("unknown hash algorithm tag in beginClearText: " + hashAlgorithm);
            }

            string armorHdr = "-----BEGIN PGP SIGNED MESSAGE-----" + nl;
            string hdrs = "Hash: " + hash + nl + nl;

            DoWrite(armorHdr);
            DoWrite(hdrs);

            clearText = true;
            newLine = true;
            lastb = 0;
        }

        public void EndClearText()
        {
            clearText = false;
        }

        public override void WriteByte(
            byte    value)
        {
            if (clearText)
            {
                outStream.WriteByte(value);

                if (newLine)
                {
                    if (!(value == '\n' && lastb == '\r'))
                    {
                        newLine = false;
                    }
                    if (value == '-')
                    {
                        outStream.WriteByte((byte)' ');
                        outStream.WriteByte((byte)'-');      // dash escape
                    }
                }
                if (value == '\r' || (value == '\n' && lastb != '\r'))
                {
                    newLine = true;
                }
                lastb = value;
                return;
            }

            if (start)
            {
                bool        newPacket = (value & 0x40) != 0;
                int         tag = 0;

                if (newPacket)
                {
                    tag = value & 0x3f;
                }
                else
                {
                    tag = (value & 0x3f) >> 2;
                }

                switch ((PacketTag)tag)
                {
                case PacketTag.PublicKey:
                    type = "PUBLIC KEY BLOCK";
                    break;
                case PacketTag.SecretKey:
                    type = "PRIVATE KEY BLOCK";
                    break;
                case PacketTag.Signature:
                    type = "SIGNATURE";
                    break;
                default:
                    type = "MESSAGE";
				    break;
                }

                DoWrite(headerStart + type + headerTail + nl);
                WriteHeaderEntry("Version", (string) headers["Version"]);

                foreach (DictionaryEntry de in headers)
                {
                    string k = (string) de.Key;
                    if (k != "Version")
                    {
                        string v = (string) de.Value;
                        WriteHeaderEntry(k, v);
                    }
                }

                DoWrite(nl);

                start = false;
            }

            if (bufPtr == 3)
            {
                Encode(outStream, buf, bufPtr);
                bufPtr = 0;
                if ((++chunkCount & 0xf) == 0)
                {
                    DoWrite(nl);
                }
            }

            crc.Update(value);
            buf[bufPtr++] = value & 0xff;
        }

        /**
         * <b>Note</b>: close does nor close the underlying stream. So it is possible to write
         * multiple objects using armoring to a single stream.
         */
        public override void Close()
        {
            if (type != null)
            {
                Encode(outStream, buf, bufPtr);

                DoWrite(nl + '=');

                int crcV = crc.Value;

				buf[0] = ((crcV >> 16) & 0xff);
                buf[1] = ((crcV >> 8) & 0xff);
                buf[2] = (crcV & 0xff);

                Encode(outStream, buf, 3);

                DoWrite(nl);
                DoWrite(footerStart);
                DoWrite(type);
                DoWrite(footerTail);
                DoWrite(nl);

                outStream.Flush();

                type = null;
                start = true;
				base.Close();
			}
        }

		private void WriteHeaderEntry(
			string	name,
			string	value)
        {
            DoWrite(name + ": " + value + nl);
        }

		private void DoWrite(
			string s)
        {
            foreach (char c in s)
            {
                outStream.WriteByte((byte) c);
            }
        }
    }
}
