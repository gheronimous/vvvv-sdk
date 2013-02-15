﻿/*
 * Created by SharpDevelop.
 * User: frederik
 * Date: 11/01/2013
 * Time: 15:21
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.IO;

using System.ComponentModel.Composition;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;
using VVVV.Utils.Streams;

using System.Collections.Generic;

namespace InterProcessSendReceiveNodes
{
	public enum MessageTypeEnum {
		stringSpread,
		doubleSpread,
		colorSpread,
		rawSpread //System.IO.Stream
	}
	
	/// <summary>
	/// Description of Utils.
	/// </summary>
	public static class Utils
	{
		public static string GetChannelPrefix(ISpread<string> spread) {
			return "vvvv/InterProcess/StringSpread/";
		}
		public static string GetChannelPrefix(ISpread<double> spread) {
			return "vvvv/InterProcess/DoubleSpread/";
		}
		public static string GetChannelPrefix(ISpread<RGBAColor> spread) {
			return "vvvv/InterProcess/ColorSpread/";
		}
		public static string GetChannelPrefix(IStream spread) {
			return "vvvv/InterProcess/StreamSpread/";
		}
		
		
		
		public static byte[] GenerateMessage(ISpread<string> spread, uint version) {
			List<byte> bytes = new List<byte>();

			//message type
			bytes.AddRange(BitConverter.GetBytes((UInt32)MessageTypeEnum.stringSpread));

			//message version
			bytes.AddRange(BitConverter.GetBytes((UInt32)version));
			
			//nr of slices
			bytes.AddRange(BitConverter.GetBytes((Int32)spread.SliceCount));
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//string length
				bytes.AddRange( BitConverter.GetBytes((Int32)spread[i].Length) );
				
				//string bytes
				bytes.AddRange( new System.Text.UnicodeEncoding().GetBytes(spread[i]) );
			}
			return bytes.ToArray();
		}

		public static void ProcessMessage(byte[] bytes, ISpread<string> spread) {
			//skip message type & version
			int pos = sizeof(uint) * 2;
			
			//Read slicecount
			spread.SliceCount = BitConverter.ToInt32(bytes, pos);
			pos += sizeof(Int32);
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//Read string length (nr of characters)
				int len = BitConverter.ToInt32(bytes, pos);
				pos += sizeof(Int32);
				
				//Read string bytes
				//BitConverter.ToChar(
				spread[i] = new System.Text.UnicodeEncoding().GetString(bytes, pos, len * sizeof(char));
				pos += len * sizeof(char);
			}
		}

		
		public static byte[] GenerateMessage(ISpread<double> spread, uint version) {
			List<byte> bytes = new List<byte>();

			//message type
			bytes.AddRange(BitConverter.GetBytes((UInt32)MessageTypeEnum.doubleSpread));

			//message version
			bytes.AddRange(BitConverter.GetBytes((UInt32)version));
			
			//nr of slices
			bytes.AddRange(BitConverter.GetBytes((Int32)spread.SliceCount));
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//float bytes
				bytes.AddRange( BitConverter.GetBytes((Double)spread[i]) );
			}
			return bytes.ToArray();
		}

		public static void ProcessMessage(byte[] bytes, ISpread<double> spread) {
			//skip message type & version
			int pos = sizeof(uint) * 2;
			
			//Read slicecount
			spread.SliceCount = BitConverter.ToInt32(bytes, pos);
			pos += sizeof(Int32);
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//Read Double bytes
				spread[i] = BitConverter.ToDouble(bytes, pos);
				pos += sizeof(Double);
			}
		}


		public static byte[] GenerateMessage(ISpread<RGBAColor> spread, uint version) {
			List<byte> bytes = new List<byte>();

			//message type
			bytes.AddRange(BitConverter.GetBytes((UInt32)MessageTypeEnum.colorSpread));

			//message version
			bytes.AddRange(BitConverter.GetBytes((UInt32)version));
			
			//nr of slices
			bytes.AddRange(BitConverter.GetBytes((Int32)spread.SliceCount));
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {			
				//color bytes
				bytes.AddRange( BitConverter.GetBytes((Double)spread[i].R) );
				bytes.AddRange( BitConverter.GetBytes((Double)spread[i].G) );
				bytes.AddRange( BitConverter.GetBytes((Double)spread[i].B) );
				bytes.AddRange( BitConverter.GetBytes((Double)spread[i].A) );
			}
			return bytes.ToArray();
		}

		public static void ProcessMessage(byte[] bytes, ISpread<RGBAColor> spread) {
			//skip message type & version
			int pos = sizeof(uint) * 2;
			
			//Read slicecount
			spread.SliceCount = BitConverter.ToInt32(bytes, pos);
			pos += sizeof(Int32);
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//Read RGBAColor bytes
				RGBAColor c = new RGBAColor();
				c.R = BitConverter.ToDouble(bytes, pos);
				pos += sizeof(Double);
				c.G = BitConverter.ToDouble(bytes, pos);
				pos += sizeof(Double);
				c.B = BitConverter.ToDouble(bytes, pos);
				pos += sizeof(Double);
				c.A = BitConverter.ToDouble(bytes, pos);
				pos += sizeof(Double);
				
				spread[i] = c;
			}
		}

		
		public static byte[] GenerateMessage(IInStream<Stream> spread, uint version) {
			//message type
			byte[] typeBytes = BitConverter.GetBytes((UInt32)MessageTypeEnum.rawSpread);

			//message version
			byte[] versionBytes = BitConverter.GetBytes((UInt32)version);
			
			//nr of slices
			byte[] sliceCountBytes = BitConverter.GetBytes((Int32)spread.SliceCount);

			
			//first calculate the size
			long size = typeBytes.Length + versionBytes.Length + sliceCountBytes.Length;
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//stream length
				size += sizeof(long);
				size += spread[i].Length;
			}
			
			
			byte[] bytes = new byte[size];

			int index = 0;
			typeBytes.CopyTo(bytes, index);
			index += typeBytes.Length;
			versionBytes.CopyTo(bytes, index);
			index += versionBytes.Length;
			sliceCountBytes.CopyTo(bytes, index);
			index += sliceCountBytes.Length;
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//stream length
				BitConverter.GetBytes( spread[i].Length ).CopyTo(bytes, index);
				index += sizeof(long);
				
				//stream bytes
				spread[i].
				spread[i].Read( bytes, index, (Int32)(spread[i].Length) );
//				index += (Int32)spread[i].Length;
			}
			
			return bytes;
		}
		
		public static void ProcessMessage(byte[] bytes, IOutStream<Stream> spread) {
			//skip message type & version
			int pos = sizeof(uint) * 2;
			
			//Read slicecount
			spread.SliceCount = BitConverter.ToInt32(bytes, pos);
			pos += sizeof(Int32);
			
			for ( int i = 0; i < spread.SliceCount; i++ ) {
				//Read Stream nr of bytes
				Int32 streamLength = BitConverter.ToInt32(bytes, pos);
				pos += sizeof(Int32);

				//Read Stream bytes
				spread[i] = new MemoryStream();
				spread[i].Read(bytes, pos, streamLength);
				pos += streamLength;
			}
		}

		
		
		
		public static MessageTypeEnum GetMessageType(byte[] bytes) {
			//Read MessageType
			return (MessageTypeEnum)BitConverter.ToUInt32(bytes, 0);
		}
		
		public static uint GetVersion(byte[] bytes) {
			//Read version
			return BitConverter.ToUInt32(bytes, sizeof(UInt32));
		}
		
		

	}
}