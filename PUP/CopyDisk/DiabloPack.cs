/*  
    This file is part of IFS.

    IFS is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    IFS is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with IFS.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IO;

namespace IFS.CopyDisk
{
    public struct DiskGeometry
    {
        public DiskGeometry(DiabloDiskType type)
        {
            bool isDiablo31 = type == DiabloDiskType.Diablo31;
            bool isDiablo44Dolphin = type == DiabloDiskType.Diablo44Dolphin;

            Cylinders = isDiablo31 ? 203 : 406;

            Tracks = 2; // true until we start dealing with Trident images

            // The Dolphin's special (emulated) Diablo 44 variant has 14 sectors/track, not 12.
            Sectors = isDiablo44Dolphin ? 14 : 12;

            TotalSizeInBytes = Cylinders * Tracks * Sectors * DiabloDiskSector.TotalSizeInBytes;
        }

        public readonly int Cylinders;
        public readonly int Tracks;
        public readonly int Sectors;

        // Size of the resulting disk image in bytes
        public readonly int TotalSizeInBytes;
    }

    public enum DiabloDiskType
    {
        Diablo31,
        Diablo44,
        Diablo44Dolphin
    }

    public class DiabloDiskSector
    {
        public DiabloDiskSector(byte[] header, byte[] label, byte[] data)
        {
            if (header.Length != 4 ||
                label.Length != 16 ||
                data.Length != 512)
            {
                throw new InvalidOperationException("Invalid sector header/label/data length.");
            }

            Header = header;
            Label = label;
            Data = data;
        }       

        public byte[] Header;
        public byte[] Label;
        public byte[] Data;

        public static DiabloDiskSector Empty = new DiabloDiskSector(new byte[4], new byte[16], new byte[512]);

        // + 2 because of the extra word used by the disk image format
        public static int TotalSizeInBytes = 4 + 16 + 512 + 2;
    }

    /// <summary>
    /// Encapsulates disk image data for all disk packs used with the
    /// standard Alto Disk Controller (i.e. the 31 and 44, which differ
    /// only in the number of cylinders)
    /// </summary>
    public class DiabloPack
    {
        public DiabloPack(DiabloDiskType type)
        {
            _diskType = type;
            _packName = null;
            
            _geometry = new DiskGeometry(type);
            _sectors = new DiabloDiskSector[_geometry.Cylinders, _geometry.Tracks, _geometry.Sectors];
        }

        public DiskGeometry Geometry
        {
            get { return _geometry; }
        }

        public string PackName
        {
            get { return _packName; }
            set { _packName = value; }
        }

        public int MaxAddress
        {
            get
            {
                return _geometry.Sectors * _geometry.Tracks * _geometry.Cylinders;
            }
        }

        public static DiabloDiskType GetTypeFromImageSize(long imageSize)
        {
            int diablo31size = new DiskGeometry(DiabloDiskType.Diablo31).TotalSizeInBytes;
            int diablo44size = new DiskGeometry(DiabloDiskType.Diablo44).TotalSizeInBytes;
            int diablo44DolphinSize = new DiskGeometry(DiabloDiskType.Diablo44Dolphin).TotalSizeInBytes;

            if (imageSize == diablo31size)
            {
                return DiabloDiskType.Diablo31;
            }
            else if (imageSize == diablo44size)
            {
                return DiabloDiskType.Diablo44;
            }
            else if (imageSize == diablo44DolphinSize)
            {
                return DiabloDiskType.Diablo44Dolphin;
            }
            else
            {
                return DiabloDiskType.Diablo31;
            }
        }

        public void Load(Stream imageStream, string path, bool reverseByteOrder)
        {
            _packName = path;
            for(int cylinder = 0; cylinder < _geometry.Cylinders; cylinder++)
            {
                for(int track = 0; track < _geometry.Tracks; track++)
                {
                    for(int sector = 0; sector < _geometry.Sectors; sector++)
                    {
                        byte[] header = new byte[4];        // 2 words
                        byte[] label = new byte[16];        // 8 words
                        byte[] data = new byte[512];        // 256 words

                        //
                        // Bitsavers images have an extra word in the header for some reason.
                        // ignore it.
                        // TODO: should support different formats ("correct" raw, Alto CopyDisk format, etc.)
                        //
                        imageStream.Seek(2, SeekOrigin.Current);

                        if (imageStream.Read(header, 0, header.Length) != header.Length)
                        {
                            throw new InvalidOperationException("Short read while reading sector header.");
                        }

                        if (imageStream.Read(label, 0, label.Length) != label.Length)
                        {
                            throw new InvalidOperationException("Short read while reading sector label.");
                        }

                        if (imageStream.Read(data, 0, data.Length) != data.Length)
                        {
                            throw new InvalidOperationException("Short read while reading sector data.");
                        }                      

                        _sectors[cylinder, track, sector] = 
                            new DiabloDiskSector(
                                reverseByteOrder ? SwapBytes(header) : header,
                                reverseByteOrder ? SwapBytes(label) : label,
                                reverseByteOrder ? SwapBytes(data) : data);                        
                    }
                }
            }

            if (imageStream.Position != imageStream.Length)
            {
                throw new InvalidOperationException("Extra data at end of image file.");
            }
        }

        public void Save(Stream imageStream, bool reverseByteOrder)
        {
            byte[] emptyHeader = new byte[4];        // 2 words
            byte[] emptyLabel = new byte[16];        // 8 words
            byte[] emptyData = new byte[512];        // 256 words

            for (int cylinder = 0; cylinder < _geometry.Cylinders; cylinder++)
            {
                for (int track = 0; track < _geometry.Tracks; track++)
                {
                    for (int sector = 0; sector < _geometry.Sectors; sector++)
                    {

                        //
                        // Bitsavers images have an extra word in the header for some reason.
                        // We will follow this 'standard' when writing out.
                        // TODO: should support different formats ("correct" raw, Alto CopyDisk format, etc.)
                        //
                        byte[] dummy = new byte[2];
                        imageStream.Write(dummy, 0, 2);

                        DiabloDiskSector s = GetSector(cylinder, track, sector);

                        imageStream.Write(reverseByteOrder ? SwapBytes(s.Header) : s.Header, 0, s.Header.Length);
                        imageStream.Write(reverseByteOrder ? SwapBytes(s.Label) : s.Label, 0, s.Label.Length);
                        imageStream.Write(reverseByteOrder ? SwapBytes(s.Data) : s.Data, 0, s.Data.Length);
                        
                    }
                }
            }
        }

        public int DiskAddressToVirtualAddress(ushort diskAddress)
        {
            int head = (diskAddress & 0x4) >> 2;
            int cylinder = (diskAddress & 0xff8) >> 3;
            int sector = (diskAddress & 0xf000) >> 12;

            return cylinder * (_geometry.Sectors * _geometry.Tracks) + head * _geometry.Sectors + sector;
        }

        public DiabloDiskSector GetSector(int cylinder, int track, int sector)
        {
            DiabloDiskSector s = _sectors[cylinder, track, sector];

            // For invalid / empty sectors, return an Empty sector.
            if (s == null)
            {
                s = DiabloDiskSector.Empty;
            }

            return s;
        }

        public void SetSector(int cylinder, int track, int sector, DiabloDiskSector newSector)
        {            
            _sectors[cylinder, track, sector] = newSector;
        }

        public DiabloDiskSector GetSector(int address)
        {
            if (address < 0 || address >= MaxAddress)
            {
                throw new InvalidOperationException("Disk address is out of range.");
            }
            
            int sector = address % _geometry.Sectors;
            int track = (address / _geometry.Sectors) % _geometry.Tracks;
            int cylinder = (address / (_geometry.Sectors * _geometry.Tracks));

            return GetSector(cylinder, track, sector);
        }

        public void SetSector(int address, DiabloDiskSector newSector)
        {
            if (address < 0 || address >= MaxAddress)
            {
                throw new InvalidOperationException("Disk address is out of range.");
            }

            int sector = address % _geometry.Sectors;
            int track = (address / _geometry.Sectors) % _geometry.Tracks;
            int cylinder = (address / (_geometry.Sectors * _geometry.Tracks));            

            SetSector(cylinder, track, sector, newSector);
        }

        private byte[] SwapBytes(byte[] data)
        {
            byte[] swapped = new byte[data.Length];
            for(int i=0;i<data.Length;i+=2)
            {                
                swapped[i] = data[i + 1];
                swapped[i + 1] = data[i];
            }

            return swapped;
        }

        private string _packName;               // The file from whence the data came originally
        private DiabloDiskSector[,,] _sectors;
        private DiabloDiskType _diskType;
        private DiskGeometry _geometry;
    }
}
