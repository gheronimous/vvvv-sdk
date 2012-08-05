﻿/*
 * Created by SharpDevelop.
 * User: frederik
 * Date: 29/02/2012
 * Time: 17:54
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;
using System.Threading;

using SlimDX;
using SlimDX.Direct3D9;

using VVVV.Core.Logging;

using VVVV.Nodes.Vlc.Utils;


namespace VVVV.Nodes.Vlc.Player
{
	/// <summary>
	/// Description of MemoryToTextureRenderer.
	/// </summary>
	public class MemoryToTextureRenderer : IDisposable
	{
		private char group;
		
		private C0_5EX9_TextureVlcNode parent;
		private DoubleMemoryBuffer doubleBuffer;
		private Dictionary<Device, DoubleTexture> device2DoubleTexture;
		private int slice;

		private ReaderWriterLock textureLock = new ReaderWriterLock();
		//lock used for displaying => switching between the 2 buffers
		private bool textureNeedsResizingOnEvaluate;
		//if the memory buffer's size changes, we need to update the textures also (in the mainloop)
		private bool deviceDataNeedsUpdatingOnEvaluate;
		//if the textures are filled (and frontback toggled), the deviceData needs to be updated (make everything point to the new front textures)
		private Mutex memoryToTextureRendererBusyMutex;
		//used for starting and stopping etc. in separate thread
		Thread updateTextureThread;
		//will work when signalled by the right EventWaitHandle
		private EventWaitHandle updateTextureEventWaitHandle;
		private EventWaitHandle updateTextureStopThreadWaitHandle;

		public MemoryToTextureRenderer(C0_5EX9_TextureVlcNode vvvvNode, int slice, char group, DoubleMemoryBuffer doubleMemoryBuffer)
		{
			this.group = group;
			this.slice = slice;
			this.doubleBuffer = doubleMemoryBuffer;
			this.parent = vvvvNode;
			device2DoubleTexture = new Dictionary<Device, DoubleTexture>();

			//We will subscribe to the event fired by DoubleMemoryBuffer
			doubleBuffer.Toggle += new DoubleMemoryBuffer.ToggleHandler(DoubleBufferChanged);

			textureNeedsResizingOnEvaluate = true;

			memoryToTextureRendererBusyMutex = new Mutex();

			//CREATE A THREAD THAT WILL TRY TO LOAD NEW FILES ETC. 
			//when signalled by evaluateEventWaitHandle
			updateTextureEventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
			updateTextureStopThreadWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
			updateTextureThread = new Thread(new ThreadStart(UpdateTexture_ThreadProc));
			updateTextureThread.Start();
		}

		~MemoryToTextureRenderer()
		{
			Dispose();
		}

		public void Dispose()
		{
			updateTextureStopThreadWaitHandle.Set();
			updateTextureThread.Join();
			
			//cleanup all data we created
			try {
				//Log( LogType.Debug, "[CleanupDevice2DoubleTexture] current device " + device.ComPointer.ToInt64() );
				foreach (Device d in device2DoubleTexture.Keys) {
					device2DoubleTexture[d].Dispose();
				}
			} catch (Exception e) {
				Log(LogType.Error, "[Dispose] " + e.Message);
			}

		}

		//Called every vvvv frame, to do stuff that's only safe to do in the mainloop
		public void Evaluate()
		{
			prevTime = DateTime.Now.Ticks;
			//for ReportElapsedTime
			if ( memoryToTextureRendererBusyMutex.WaitOne(200) ) {
				try {
					ReportElapsedTime("Entering MUTEX", 15.7);
					//prevTime = DateTime.Now.Ticks; //for ReportElapsedTime
					if (textureNeedsResizingOnEvaluate) {
						Log(LogType.Debug, "Texture needs to be resized to " + doubleBuffer.GetWidth() + "x" + doubleBuffer.GetHeight());
						if ( ResizeTextureIfNecessary() ) {
							//Log( LogType.Debug, "resizing successful..." );
							textureNeedsResizingOnEvaluate = false;
							UpdateTexture_Threaded();
						}
						ReportElapsedTime("Resizing textures", 15.7);
					}

					//only if 'front' renderer
					if ( this == parent.GetMemoryToTextureRendererCurrent(slice) ) {
						if (deviceDataNeedsUpdatingOnEvaluate) {
							UpdateDeviceData();
	
							deviceDataNeedsUpdatingOnEvaluate = false;
						}
					}

					ReportElapsedTime("Updating device data", 15.7);
				}
				catch (Exception e) {
					Log(LogType.Debug, "[Evaluate] Exception" + e.Message + "\n" + e.StackTrace);
				}
				finally {
					memoryToTextureRendererBusyMutex.ReleaseMutex();
				}
			} else {
				ReportElapsedTime("[Evaluate] Entering MUTEX FAILED and it ", 0);
			}
		}

		private void DoubleBufferChanged()
		{
			//Log( LogType.Debug, "DoubleBufferChanged" );
			UpdateTexture_Threaded();
		}

		public void UpdateTexture_ThreadProc()
		{
			while (true) {
				int waitHandleIndex = WaitHandle.WaitAny(new EventWaitHandle[2] {
					updateTextureEventWaitHandle,
					updateTextureStopThreadWaitHandle
				});

				if (waitHandleIndex == 0) {
					try {
						UpdateTexture(null);
					} catch (Exception e) {
						Log(LogType.Error, "[UpdateTexture] Something went terribly wrong: " + e.Message + "\n" + e.StackTrace);
					}
					//Thread.Sleep(2);
				} else if (waitHandleIndex == 1) {
					if (memoryToTextureRendererBusyMutex.WaitOne(10000)) {
						memoryToTextureRendererBusyMutex.ReleaseMutex();
					} else {

					}
					break;
				}
			}
			Log(LogType.Debug, "... exiting updateTexture thread for memoryToTextureRenderer " + slice + " ... ");
		}

		public void UpdateTexture_Threaded()
		{
			//ThreadPool.QueueUserWorkItem( UpdateTexture, null );
			updateTextureEventWaitHandle.Set();
		}

		public void UpdateTexture()
		{
			UpdateTexture(null);
		}

		private void UpdateTexture(object unused)
		{
			prevTime = DateTime.Now.Ticks; //for ReportElapsedTime
			if (memoryToTextureRendererBusyMutex.WaitOne(50)) {
				//we will copy this information to the texture, but ONLY if the sizes match!
				//resizing and updating device data, will be done in the main thread (in Evaluate(), 
				//to make sure we don't get funky results)
				CopyMemoryToTexture();
				memoryToTextureRendererBusyMutex.ReleaseMutex();
			} else {
				ReportElapsedTime("Entering MUTEX FAILED and it ", 0);
			}
		}

		private bool ResizeTextureIfNecessary()
		{
			bool success = true;
			//handle resize
			if (doubleBuffer.LockFrontBufferForReading(500)) {
				//foreach ( DoubleTexture t2 in device2DoubleTexture.Values ) {
				foreach (Device device in new List<Device>(device2DoubleTexture.Keys) ) {
//						DoubleTexture t2;
//						if (device2DoubleTexture.TryGetValue(device, out t2)) {
						//Log(LogType.Debug, "SetNewSize on DoubleTexture (device " + t2.GetDevice().ComPointer.ToInt64() + "=" + deviceId + ", " + t2.GetWidth() + "x" + t2.GetHeight() +  ")");
//							int result = t2.SetNewSize(doubleBuffer.GetWidth(), doubleBuffer.GetHeight());
//							success = success && (0 == result);
//							if (result != 0) {
//								Log(LogType.Error, "SetNewSize on DoubleTexture FAILED. Size = " + t2.GetWidth() + "x" + t2.GetHeight() + " but it should be " + doubleBuffer.GetWidth() + "x" + doubleBuffer.GetHeight() + " return value: " + result);
//							}
//						}
					
					DoubleTexture t2;
					if 	( 	device2DoubleTexture.TryGetValue(device, out t2) 
						 && ( t2.GetWidth() != doubleBuffer.GetWidth() || t2.GetHeight() != doubleBuffer.GetHeight() ) 
						) {
						//remove old texture (will be done when a new one is created)
						//device2DoubleTexture[device].Dispose();
						//create a new one
						if ( CreateTexture( device ) == null ) {
							success = false;
						}
						else {
							Log(LogType.Debug, "Setting deviceDataNeedsUpdatingOnEvaluate from " + deviceDataNeedsUpdatingOnEvaluate + " to true!");
							deviceDataNeedsUpdatingOnEvaluate = true;
						}
					}
				}
				doubleBuffer.UnlockFrontBuffer();
			}
			return success;
		}

		private void CopyMemoryToTexture()
		{
			FillTextureUsingLockRectangle();
		}

		public Texture CreateTexture(Device device)
		{
			Texture retVal = null;
							
			//return CreateManagedTexture(device, Math.Max( this.doubleBuffer.GetWidth(), 1 ), Math.Max( this.doubleBuffer.GetHeight(), 1 ) );
			if (memoryToTextureRendererBusyMutex.WaitOne()) {
				//Log(LogType.Debug, "CreateTexture on device " + device.ComPointer.ToInt64() + " ---------------------------------------------------------------- ");	
				DoubleTexture t2 = CreateDoubleTexture(device);
				
/*						//Log(LogType.Debug, "CreateTexture on device " + device.ComPointer.ToInt64() + "=" + t2.GetDevice().ComPointer.ToInt64() + " ---------------------------------------------------------------- ");
					foreach (Device d in device2DoubleTexture.Keys) {
						DoubleTexture t2Temp;
						if (device2DoubleTexture.TryGetValue(d, out t2Temp)) {
							Log(LogType.Debug, "\tDoubleTexture (device " + t2.GetDevice().ComPointer.ToInt64() + "=" + device.ComPointer.ToInt64() + ", " + t2Temp.GetWidth() + "x" + t2Temp.GetHeight() + ")");
						}
					}
*/
				memoryToTextureRendererBusyMutex.ReleaseMutex();

				retVal = t2 != null ? t2.GetFrontTexture() : null;
			} 
			else {
				Log(LogType.Error, "[CreateTexture ERROR] CreateTexture(device " + device.ComPointer.ToInt64() + ") FAILED because mutex not available !!!");
				//This shouldn't happen
				int w = Math.Max(this.doubleBuffer.GetWidth(), 1);
				int h = Math.Max(this.doubleBuffer.GetHeight(), 1);
				return VlcUtils.CreateManagedTexture(device, w, h);
			}
			
			if (retVal != null) {
				//Log(LogType.Debug, "[CreateTexture] done, trying to update.");
				UpdateTexture_Threaded();
				//Log(LogType.Debug, "[CreateTexture] update should be ok. ");
			}
			else {					
				Log(LogType.Error, "CreateTexture(" + device.ComPointer.ToInt64() + " FAILED");
			}
			
			return retVal;
//				return CreateManagedTexture(device, Math.Max(this.doubleBuffer.GetWidth(), 1), Math.Max(this.doubleBuffer.GetHeight(), 1));
		}

		private DoubleTexture CreateDoubleTexture(Device device)
		{
			int w = Math.Max(this.doubleBuffer.GetWidth(), 2);
			int h = Math.Max(this.doubleBuffer.GetHeight(), 2);
			Log(LogType.Debug, "CreateDoubleTexture(" + " device " + device.ComPointer.ToInt64() + ", " + w + "x" + h + " ) called ");

			DoubleTexture t2 = null;

			try {
				t2 = new DoubleTexture(device, w, h);
				//Log( LogType.Debug , "DoubleTexture created..." );
				Log(LogType.Debug, 					/*(this == parent.mediaRendererA ? "A " : "B ") + (active ? "(FRONT) " + parent.FFileNameIn[slice] : "(BACK) " + parent.FNextFileNameIn[slice] ) + */" Created new texture (" + t2.GetWidth() + "x" + t2.GetHeight() + ") for device " + device.ComPointer.ToInt64() + ". ");
				if (t2 != null) {
					try {
						DoubleTexture t2Temp;
						if (device2DoubleTexture.TryGetValue(device, out t2Temp)) {
							Log(LogType.Debug, "\tTrying to dispose old DoubleTexture (device  " + device.ComPointer.ToInt64() + ", " + t2Temp.GetWidth() + "x" + t2Temp.GetHeight() + ")");
							t2Temp.Dispose();
						}
					} catch {
						Log(LogType.Debug, "\tfailed");
					}

					device2DoubleTexture[device] = t2;
					Log(LogType.Debug, "Added new DoubleTexture to device2DoubleTexture...");
				}
				//Log( LogType.Debug , (this == parent.mediaRendererA ? "A " : "B ") + (active ? "(FRONT) " + parent.FFileNameIn[GetMediaRendererIndex()] : "(BACK) " + parent.FNextFileNameIn[GetMediaRendererIndex()] ) + " Created new texture (" + w + "x" + h + ") for device " + device.ComPointer.ToInt64() + ". " );
			} catch (Exception e) {
				Log(LogType.Error, "[CreateDoubleTexture Exception] " + e.Message);
				//t2 = new DoubleTexture(device, 2, 2);
			}

			return t2;
		}

		/**
		 * Used to show the correct textures
		 */
		private void UpdateDeviceData()
		{
			try {
				CleanupDevice2DoubleTexture();

				foreach (Device k in parent.GetDeviceData().Keys) {
					//Log( LogType.Debug, "k = " + k );
					DoubleTexture t2;
					//Device device = parent.FDeviceData[k].Data[slice].Device;
					if (device2DoubleTexture.TryGetValue(k, out t2)) {
						parent.GetDeviceData()[k].Data[slice] = t2.GetFrontTexture();
					}
				}
			} catch (Exception e) {
				Log(LogType.Error, "[UpdateDeviceData Exception] " + e.Message);
			}
		}

		
		/** parameter = current device (this device should NOT be removed)
		 */
		private void CleanupDevice2DoubleTexture()
		{
			try {
				//Log( LogType.Debug, "[CleanupDevice2DoubleTexture] current device " + device.ComPointer.ToInt64() );

				List<Device> devicesToDelete = new List<Device>();
				foreach (Device d in device2DoubleTexture.Keys) {
					//never remove current device
					//if (currentDevice.ComPointer.ToInt64().CompareTo(currentDevice) != 0) {
					//Log( LogType.Debug, "remove device " + d + " ?" );
					bool found = false;
					foreach (Device k in parent.GetDeviceData().Keys) {
						//Log( LogType.Debug, "  " + k + " " + ( FDeviceData[k].Data[mediaRendererIndex].Disposed ? "DISPOSED!!!" : "REMOVE DEVICE" ) );
						if (k == d) {
							found = true;
							break;
						}
					}
					if (!found) {
						//Log( LogType.Debug, "remove device " + d + " !!!!!!!!!!!!!" );
						devicesToDelete.Add(d);
					}
					//}
				}

				int x = 1;
				foreach (Device d in devicesToDelete) {
					Log(LogType.Debug, "[CleanupDevice2DoubleTexture] trying to remove device " + d + " " + (x++) + "/" + devicesToDelete.Count);
					device2DoubleTexture[d].Dispose();
					device2DoubleTexture.Remove(d);
					Log(LogType.Debug, "[CleanupDevice2DoubleTexture] removed device " + d);
				}
			} catch (Exception e) {
				Log(LogType.Error, "[CleanupDevice2DoubleTexture Exception (FDeviceData cleanup)] " + e.Message);
			}
		}


		public Texture GetFrontTexture(Device d)
		{
			return device2DoubleTexture[d].GetFrontTexture();
		}

		private void FillTextureUsingLockRectangle()
		{
			if (doubleBuffer.LockFrontBufferForReading(500)) {
				try {
					foreach (DoubleTexture t2 in device2DoubleTexture.Values) {
						//Log(LogType.Debug, " fill doubletexture " + t2.GetBackTexture().ComPointer.ToInt64() + "\n");
						try {
							if (t2.LockBackTextureForWriting(100)) {
								if (t2.GetBackTexture() == null) {
									Log(LogType.Error, "[FillTextureUsingLockRectangle ERROR] " + "TEXTURE == null !!!");
								} else if (t2.GetBackTexture() != null && t2.GetBackTexture().Disposed) {
									Log(LogType.Error, "[FillTextureUsingLockRectangle ERROR] " + "TEXTURE disposed !!!");
								} else if (t2.GetWidth() != doubleBuffer.GetWidth() || t2.GetHeight() != doubleBuffer.GetHeight()) {
									textureNeedsResizingOnEvaluate = true;
									//Log(LogType.Debug, "[FillTextureUsingLockRectangle WARNING] " + "TEXTURE size wrong !");
								} else {
									//DataRectangle rDst = t2.GetBackTexture().LockRectangle(0, LockFlags.Discard);
									//rDst.Data.WriteRange( GetReadPixelPlane(), t2.GetPitch() * t2.GetHeight() );
									//t2.GetBackTexture().UnlockRectangle(0);

									DataRectangle rDst = null;
									try {
										rDst = t2.GetBackTexture().LockRectangle(0, LockFlags.Discard);
									} catch {
										Log(LogType.Error, "lockrectangle failed");
									}
									try {
										rDst.Data.WriteRange(doubleBuffer.GetFrontBuffer(), t2.GetPitch() * t2.GetHeight());
									} catch {
										Log(LogType.Error, "writerange failed");
									}
									try {
										t2.GetBackTexture().UnlockRectangle(0);
									} catch {
										Log(LogType.Error, "UNlockrectangle failed");
									}

									t2.UnlockBackTexture();

									if (t2.ToggleFrontBack()) {
										deviceDataNeedsUpdatingOnEvaluate = true;
									}

								}
								t2.UnlockBackTexture();

							}
						} catch (Exception e) {
							Log(LogType.Error, "[FillTextureUsingLockRectangle ERROR] " + e.Message);
						}
					}
				} catch (Exception e) {
					Log(LogType.Error, "[FillTextureUsingLockRectangle ERROR] (source) " + e.Message);
				}

				doubleBuffer.UnlockFrontBuffer();
			}

		}
		
		public int GetSlice()
		{
			return slice;
		}


		private void Log(LogType logType, string message)
		{
			parent.Log(logType, "[MemoryToTextureRenderer " + group + slice + ( parent.IsFrontMemoryToTextureRenderer( this ) ? "+" : "-") + "] " + message);
		}

		private long prevTime = DateTime.Now.Ticks;
		private long currTime = DateTime.Now.Ticks;
		private double ReportElapsedTime(string description, double reportOnlyIfMoreThanOrEqualToMillis)
		{
			currTime = DateTime.Now.Ticks;

			double ms = (double)(currTime - prevTime) / 10000;
			if (ms >= reportOnlyIfMoreThanOrEqualToMillis) {
				Log(LogType.Debug, description + " took " + ms + " milliseconds.");
			}
			prevTime = currTime;

			return ms;
		}

	}

}