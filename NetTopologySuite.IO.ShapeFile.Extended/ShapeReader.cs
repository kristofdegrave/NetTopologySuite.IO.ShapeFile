﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using GeoAPI.Geometries;
using NetTopologySuite.IO.Handlers;
using NetTopologySuite.IO.Streams;

namespace NetTopologySuite.IO.ShapeFile.Extended
{
    /// <summary>
    /// A class to read from a set of files forming the ESRI-Shapefile
    /// </summary>
    public class ShapeReader : IDisposable
    {
        const long HEADER_LENGTH = 100;

        private BigEndianBinaryReader m_ShapeFileReader;

        //private readonly string m_ShapeFilePath;
        private readonly IStreamProviderRegistry m_StreamProviderRegistry;
        private readonly ShapeHandler m_ShapeHandler;
        private readonly Lazy<long[]> m_ShapeOffsetCache;
        private bool m_IsDisposed;

        /// <summary>
        /// Creates an instance of this class to read from the Shapefile set of files defined by <paramref name="shapefilePath"/>
        /// </summary>
        /// <param name="shapefilePath">The path to the Shapefile</param>
        public ShapeReader(string shapefilePath) : this(new ShapefileStreamProviderRegistry(shapefilePath, true))
        {
        }

        public ShapeReader(IStreamProviderRegistry streamProviderRegistry)
        {
            if (streamProviderRegistry == null)
                throw new ArgumentNullException("streamProviderRegistry");

            m_StreamProviderRegistry = streamProviderRegistry;

            ShapefileHeader = new ShapefileHeader(ShapeReaderStream);
            m_ShapeHandler = Shapefile.GetShapeHandler(ShapefileHeader.ShapeType);

            m_ShapeOffsetCache = new Lazy<long[]>(BuildOffsetCache, LazyThreadSafetyMode.ExecutionAndPublication);

        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~ShapeReader()
        {
            Dispose(false);
        }

        /// <summary>
        /// Dispose method
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_IsDisposed = true;
            CloseShapeFileHandle();
        }
        /// <summary>
        /// Gets a value indicating the header of the main Shapefile (*.shp)
        /// </summary>
        public ShapefileHeader ShapefileHeader { get; }

        private BigEndianBinaryReader ShapeReaderStream
        {
            get
            {
                ThrowIfDisposed();

                if (m_ShapeFileReader == null)
                {
                    lock (m_StreamProviderRegistry)
                    {
                        if (m_ShapeFileReader == null)
                        {
                            m_ShapeFileReader = new BigEndianBinaryReader(m_StreamProviderRegistry[StreamTypes.Shape].OpenRead());
                        }
                    }
                }

                return m_ShapeFileReader;
            }
        }

        /// <summary>
        /// Function to read the bounding boxes of all geometries in the Shapefile (*.shp)
        /// </summary>
        /// <returns>An enumeration of bounding boxes</returns>
        public IEnumerable<MBRInfo> ReadMBRs()
        {
            ThrowIfDisposed();

            lock (m_StreamProviderRegistry)
            {
                var newReader = new BigEndianBinaryReader(m_StreamProviderRegistry[StreamTypes.Shape].OpenRead());
                return m_ShapeHandler.ReadMBRs(newReader);
            }
        }

        public IEnumerable<IGeometry> ReadAllShapes(IGeometryFactory geoFactory)
        {
            ThrowIfDisposed();

            if (geoFactory == null)
            {
                throw new ArgumentNullException("geoFactory");
            }

            return m_ShapeOffsetCache.Value.Select(offset => ReadShapeAtOffset(offset, geoFactory));
        }

        public IGeometry ReadShapeAtIndex(int index, IGeometryFactory geoFactory)
        {
            ThrowIfDisposed();

            if (geoFactory == null)
            {
                throw new ArgumentNullException("geoFactory");
            }

            if (index < 0 || index >= m_ShapeOffsetCache.Value.Length)
            {
                throw new ArgumentOutOfRangeException("index");
            }

            return ReadShapeAtOffset(m_ShapeOffsetCache.Value[index], geoFactory);
        }

        /// <summary>
        /// Read shape at a given offset.
        /// </summary>
        /// <param name="shapeOffset"> The offset at which the requested shape metadata begins.</param>
        /// <param name="geoFactory"></param>
        /// <returns></returns>
        public IGeometry ReadShapeAtOffset(long shapeOffset, IGeometryFactory geoFactory)
        {
            IGeometry currGeomtry = null;
            ThrowIfDisposed();

            if (shapeOffset < HEADER_LENGTH || shapeOffset >= ShapeReaderStream.BaseStream.Length)
            {
                throw new IndexOutOfRangeException("Shape offset cannot be lower than header length (100) or higher than shape file size");
            }

            lock (ShapeReaderStream)
            {
                // Skip to shape size location in file.
                ShapeReaderStream.BaseStream.Seek(shapeOffset + 4, SeekOrigin.Begin);

                int currShapeLengthInWords = ShapeReaderStream.ReadInt32BE();

                currGeomtry = m_ShapeHandler.Read(ShapeReaderStream, currShapeLengthInWords, geoFactory);
            }

            return currGeomtry;
        }

        private long[] BuildOffsetCache()
        {
            using (var shapeFileReader = new BigEndianBinaryReader(m_StreamProviderRegistry[StreamTypes.Shape].OpenRead()))
            {
                return m_ShapeHandler.ReadMBRs(shapeFileReader)
                                     .Select(mbrInfo => mbrInfo.ShapeFileDetails.OffsetFromStartOfFile)
                                     .ToArray();
            }

        }

        private void ThrowIfDisposed()
        {
            if (m_IsDisposed)
            {
                throw new InvalidOperationException("Cannot use a disposed ShapeReader");
            }
        }

        private void CloseShapeFileHandle()
        {
            if (m_ShapeFileReader != null)
            {
                m_ShapeFileReader.Close();
                m_ShapeFileReader = null;
            }
        }
    }
}
