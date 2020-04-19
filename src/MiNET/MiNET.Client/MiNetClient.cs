﻿#region LICENSE

// The contents of this file are subject to the Common Public Attribution
// License Version 1.0. (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// https://github.com/NiclasOlofsson/MiNET/blob/master/LICENSE.
// The License is based on the Mozilla Public License Version 1.1, but Sections 14
// and 15 have been added to cover use of software over a computer network and
// provide for limited attribution for the Original Developer. In addition, Exhibit A has
// been modified to be consistent with Exhibit B.
// 
// Software distributed under the License is distributed on an "AS IS" basis,
// WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
// the specific language governing rights and limitations under the License.
// 
// The Original Code is MiNET.
// 
// The Original Developer is the Initial Developer.  The Initial Developer of
// the Original Code is Niclas Olofsson.
// 
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2020 Niclas Olofsson.
// All Rights Reserved.

#endregion

using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using fNbt;
using Jose;
using log4net;
using MiNET.Blocks;
using MiNET.Crafting;
using MiNET.Entities;
using MiNET.Items;
using MiNET.Net;
using MiNET.Net.RakNet;
using MiNET.Utils;
using MiNET.Worlds;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

//[assembly: XmlConfigurator(Watch = true)]
// This will cause log4net to look for a configuration file
// called TestApp.exe.config in the application base
// directory (i.e. the directory containing TestApp.exe)
// The config file will be watched for changes.

namespace MiNET.Client
{
	public class MiNetClient
	{
		private readonly DedicatedThreadPool _threadPool;
		private static readonly ILog Log = LogManager.GetLogger(typeof(MiNetClient));

		
		private long _clientGuid;

		public IPEndPoint ClientEndpoint { get; set; }
		public IPEndPoint ServerEndPoint { get; set; }

		public bool IsEmulator { get; set; }

		public RakConnection Connection { get; private set; }
		public bool FoundServer => Connection.FoundServer;

		public RakSession Session => Connection.ConnectionInfo.RakSessions.Values.FirstOrDefault();
		public bool IsConnected => Session?.State == ConnectionState.Connected;

		public Vector3 SpawnPoint { get; set; }
		public long EntityId { get; set; }
		public long NetworkEntityId { get; set; }
		public int ChunkRadius { get; set; } = 5;

		public LevelInfo LevelInfo { get; } = new LevelInfo();

		public ConcurrentDictionary<long, Entity> Entities { get; private set; } = new ConcurrentDictionary<long, Entity>();
		public BlockPalette BlockPalette { get; set; } = new BlockPalette();

		public PlayerLocation CurrentLocation { get; set; }


		public string Username { get; set; }
		public int ClientId { get; set; }

		public IMcpeClientMessageHandler MessageHandler { get; set; }

		public McpeClientMessageDispatcher MessageDispatcher
		{
			get => throw new NotSupportedException("Use Connection.CustomMessageHandlerFactory instead");
			set => throw new NotSupportedException("Use Connection.CustomMessageHandlerFactory instead");
		}

		public MiNetClient(IPEndPoint endPoint, string username, DedicatedThreadPool threadPool = null)
		{
			_threadPool = threadPool;
			Username = username;
			ClientId = new Random().Next();
			ServerEndPoint = endPoint;
			if (ServerEndPoint != null) Log.Info("Connecting to: " + ServerEndPoint);
			ClientEndpoint = new IPEndPoint(IPAddress.Any, 0);
			byte[] buffer = new byte[8];
			new Random().NextBytes(buffer);
			_clientGuid = BitConverter.ToInt64(buffer, 0);
		}

		public void StartClient()
		{
			var greyListManager = new GreyListManager();
			var motdProvider = new MotdProvider();

			Connection = new RakConnection(ClientEndpoint, greyListManager, motdProvider, _threadPool);
			var handlerFactory = new BedrockClientMessageHandler(Session, MessageHandler ?? new DefaultMessageHandler(this));
			handlerFactory.ConnectionAction = () => SendLogin(Username);
			Connection.CustomMessageHandlerFactory = session => handlerFactory;

			//TODO: This is bad design, need to refactor this later.
			greyListManager.ConnectionInfo = Connection.ConnectionInfo;
			var serverInfo = Connection.ConnectionInfo;
			serverInfo.MaxNumberOfPlayers = Config.GetProperty("MaxNumberOfPlayers", 10);
			serverInfo.MaxNumberOfConcurrentConnects = Config.GetProperty("MaxNumberOfConcurrentConnects", serverInfo.MaxNumberOfPlayers);

			Connection.Start();
		}

		public bool StopClient()
		{
			Connection.Stop();
			return true;
		}

		public int PlayerStatus { get; set; }

		public void SendLogin(string username)
		{
			JWT.JsonMapper = new NewtonsoftMapper();

			var clientKey = CryptoUtils.GenerateClientKey();
			byte[] data = CryptoUtils.CompressJwtBytes(CryptoUtils.EncodeJwt(username, clientKey, IsEmulator), CryptoUtils.EncodeSkinJwt(clientKey, username), CompressionLevel.Fastest);

			McpeLogin loginPacket = new McpeLogin
			{
				protocolVersion = Config.GetProperty("EnableEdu", false) ? 111 : McpeProtocolInfo.ProtocolVersion,
				payload = data
			};

			var bedrockHandler = (BedrockClientMessageHandler) Session.CustomMessageHandler;
			bedrockHandler.CryptoContext = new CryptoContext
			{
				ClientKey = clientKey,
				UseEncryption = false,
			};

			SendPacket(loginPacket);
		}

		public void InitiateEncryption(byte[] serverKey, byte[] randomKeyToken)
		{
			try
			{
				ECPublicKeyParameters remotePublicKey = (ECPublicKeyParameters)
					PublicKeyFactory.CreateKey(serverKey);

				//ECDiffieHellmanPublicKey publicKey = CryptoUtils.FromDerEncoded(serverKey);
				//Log.Debug("ServerKey (b64):\n" + serverKey);
				//Log.Debug($"Cert:\n{publicKey.ToXmlString()}");

				Log.Debug($"RANDOM TOKEN (raw):\n\n{Encoding.UTF8.GetString(randomKeyToken)}");

				//if (randomKeyToken.Length != 0)
				//{
				//	Log.Error("Lenght of random bytes: " + randomKeyToken.Length);
				//}

				var bedrockHandler = (BedrockClientMessageHandler) Session.CustomMessageHandler;

				var agreement = new ECDHBasicAgreement();
				agreement.Init(bedrockHandler.CryptoContext.ClientKey.Private);
				byte[] secret;
				using (var sha = SHA256.Create())
				{
					secret = sha.ComputeHash(randomKeyToken.Concat(agreement.CalculateAgreement(remotePublicKey).ToByteArrayUnsigned()).ToArray());
				}

				Log.Debug($"SECRET KEY (raw):\n{Encoding.UTF8.GetString(secret)}");

				// Create a decrytor to perform the stream transform.
				IBufferedCipher decryptor = CipherUtilities.GetCipher("AES/CFB8/NoPadding");
				decryptor.Init(false, new ParametersWithIV(new KeyParameter(secret), secret.Take(16).ToArray()));

				IBufferedCipher encryptor = CipherUtilities.GetCipher("AES/CFB8/NoPadding");
				encryptor.Init(true, new ParametersWithIV(new KeyParameter(secret), secret.Take(16).ToArray()));

				bedrockHandler.CryptoContext = new CryptoContext
				{
					Decryptor = decryptor,
					Encryptor = encryptor,
					UseEncryption = true,
					Key = secret
				};

				Thread.Sleep(1250);
				McpeClientToServerHandshake magic = new McpeClientToServerHandshake();
				SendPacket(magic);
			}
			catch (Exception e)
			{
				Log.Error("Initiate encryption", e);
			}
		}

		public AutoResetEvent FirstEncryptedPacketWaitHandle = new AutoResetEvent(false);

		public AutoResetEvent FirstPacketWaitHandle = new AutoResetEvent(false);

		public CommandPermission UserPermission { get; set; }

		public AutoResetEvent PlayerStatusChangedWaitHandle = new AutoResetEvent(false);

		public bool HasSpawned { get; set; }

		public ShapedRecipe _recipeToSend = null;

		public void SendCraftingEvent2()
		{
			var recipe = _recipeToSend;

			if (recipe != null)
			{
				Log.Error("Sending crafting event: " + recipe.Id);

				McpeCraftingEvent crafting = McpeCraftingEvent.CreateObject();
				crafting.windowId = 0;
				crafting.recipeType = 1;
				crafting.recipeId = recipe.Id;

				{
					ItemStacks slotData = new ItemStacks();
					for (uint i = 0; i < recipe.Input.Length; i++)
					{
						slotData.Add(recipe.Input[i]);

						McpeInventorySlot sendSlot = McpeInventorySlot.CreateObject();
						sendSlot.inventoryId = 0;
						sendSlot.slot = i;
						sendSlot.item = recipe.Input[i];
						SendPacket(sendSlot);

						//McpeContainerSetSlot setSlot = McpeContainerSetSlot.CreateObject();
						//setSlot.item = recipe.Input[i];
						//setSlot.windowId = 0;
						//setSlot.slot = (short) (i);
						//SendPackage(setSlot);
						//Log.Error("Set set slot");
					}
					crafting.input = slotData;

					{
						McpeMobEquipment eq = McpeMobEquipment.CreateObject();
						eq.runtimeEntityId = EntityId;
						eq.slot = 9;
						eq.selectedSlot = 0;
						eq.item = recipe.Input[0];
						SendPacket(eq);
						Log.Error("Set eq slot");
					}
				}
				{
					ItemStacks slotData = new ItemStacks {recipe.Result.First()};
					crafting.result = slotData;
				}

				SendPacket(crafting);
			}


			//{
			//	McpeContainerSetSlot setSlot = McpeContainerSetSlot.CreateObject();
			//	setSlot.item = new MetadataSlot(new ItemStack(new ItemDiamondAxe(0), 1));
			//	setSlot.windowId = 0;
			//	setSlot.slot = 0;
			//	SendPackage(setSlot);
			//}
			//{
			//	McpePlayerEquipment eq = McpePlayerEquipment.CreateObject();
			//	eq.entityId = _entityId;
			//	eq.slot = 9;
			//	eq.selectedSlot = 0;
			//	eq.item = new MetadataSlot(new ItemStack(new ItemDiamondAxe(0), 1));
			//	SendPackage(eq);
			//}
		}

		public void SendCraftingEvent()
		{
			var recipe = _recipeToSend;

			if (recipe != null)
			{
				{
					//McpeContainerSetSlot setSlot = McpeContainerSetSlot.CreateObject();
					//setSlot.item = new ItemBlock(new Block(17), 0) {Count = 1};
					//setSlot.windowId = 0;
					//setSlot.slot = 0;
					//SendPackage(setSlot);
				}
				{
					McpeMobEquipment eq = McpeMobEquipment.CreateObject();
					eq.runtimeEntityId = EntityId;
					eq.slot = 9;
					eq.selectedSlot = 0;
					eq.item = new ItemBlock(new Block(17), 0) {Count = 1};
					SendPacket(eq);
				}

				Log.Error("Sending crafting event: " + recipe.Id);

				McpeCraftingEvent crafting = McpeCraftingEvent.CreateObject();
				crafting.windowId = 0;
				crafting.recipeType = 1;
				crafting.recipeId = recipe.Id;

				{
					ItemStacks slotData = new ItemStacks {new ItemBlock(new Block(17), 0) {Count = 1}};
					crafting.input = slotData;
				}
				{
					ItemStacks slotData = new ItemStacks {new ItemBlock(new Block(5), 0) {Count = 1}};
					crafting.result = slotData;
				}

				SendPacket(crafting);

				//{
				//	McpeContainerSetSlot setSlot = McpeContainerSetSlot.CreateObject();
				//	setSlot.item = new MetadataSlot(new ItemStack(new ItemBlock(new Block(5), 0), 4));
				//	setSlot.windowId = 0;
				//	setSlot.slot = 0;
				//	SendPackage(setSlot);
				//}

				{
					McpeMobEquipment eq = McpeMobEquipment.CreateObject();
					eq.runtimeEntityId = EntityId;
					eq.slot = 10;
					eq.selectedSlot = 1;
					eq.item = new ItemBlock(new Block(5), 0) {Count = 1};
					SendPacket(eq);
				}
			}
		}

		public void WriteInventoryToFile(string fileName, ItemStacks slots)
		{
			Log.Info($"Writing inventory to filename: {fileName}");
			FileStream file = File.OpenWrite(fileName);

			IndentedTextWriter writer = new IndentedTextWriter(new StreamWriter(file));

			writer.WriteLine("// GENERATED CODE. DON'T EDIT BY HAND");
			writer.Indent++;
			writer.Indent++;
			writer.WriteLine("public static List<Item> CreativeInventoryItems = new List<Item>()");
			writer.WriteLine("{");
			writer.Indent++;

			foreach (var entry in slots)
			{
				var slot = entry;
				NbtCompound extraData = slot.ExtraData;
				if (extraData == null)
				{
					writer.WriteLine($"new Item({slot.Id}, {slot.Metadata}, {slot.Count}),");
				}
				else
				{
					//Log.Debug("Extradata: \n" + extraData);
					if (extraData.Contains("ench"))
					{
						NbtList ench = (NbtList) extraData["ench"];

						NbtCompound enchComp = (NbtCompound) ench[0];
						var id = enchComp["id"].ShortValue;
						var lvl = enchComp["lvl"].ShortValue;
						writer.WriteLine($"new Item({slot.Id}, {slot.Metadata}, {slot.Count}){{ExtraData = new NbtCompound {{new NbtList(\"ench\") {{new NbtCompound {{new NbtShort(\"id\", {id}), new NbtShort(\"lvl\", {lvl}) }} }} }} }},");
					}
					else if (extraData.Contains("Fireworks"))
					{
						NbtCompound fireworks = (NbtCompound) extraData["Fireworks"];
						NbtList explosions = (NbtList) fireworks["Explosions"];
						byte flight = fireworks["Flight"].ByteValue;
						if (explosions.Count > 0)
						{
							NbtCompound compound = (NbtCompound) explosions[0];
							byte[] fireworkColor = compound["FireworkColor"].ByteArrayValue;
							byte[] fireworkFade = compound["FireworkFade"].ByteArrayValue;
							byte fireworkFlicker = compound["FireworkFlicker"].ByteValue;
							byte fireworkTrail = compound["FireworkTrail"].ByteValue;
							byte fireworkType = compound["FireworkType"].ByteValue;

							writer.WriteLine($"new Item({slot.Id}, {slot.Metadata}, {slot.Count}){{ExtraData = new NbtCompound {{ new NbtCompound(\"Fireworks\") {{ new NbtList(\"Explosions\") {{ new NbtCompound {{ new NbtByteArray(\"FireworkColor\", new byte[]{{{fireworkColor[0]}}}), new NbtByteArray(\"FireworkFade\", new byte[0]), new NbtByte(\"FireworkFlicker\", {fireworkFlicker}), new NbtByte(\"FireworkTrail\", {fireworkTrail}), new NbtByte(\"FireworkType\", {fireworkType})  }} }}, new NbtByte(\"Flight\", {flight}) }} }} }},");
						}
						else
						{
							writer.WriteLine($"new Item({slot.Id}, {slot.Metadata}, {slot.Count}){{ExtraData = new NbtCompound {{new NbtCompound(\"Fireworks\") {{new NbtList(\"Explosions\", NbtTagType.Compound), new NbtByte(\"Flight\", {flight}) }} }} }},");
						}
					}
				}
			}

			// Template
			new ItemAir
			{
				ExtraData = new NbtCompound
				{
					new NbtList("ench")
					{
						new NbtCompound
						{
							new NbtShort("id", 0),
							new NbtShort("lvl", 0)
						}
					}
				}
			};
			//var compound = new NbtCompound(string.Empty) { new NbtList("ench", new NbtCompound()) {new NbtShort("id", 0),new NbtShort("lvl", 0),}, };

			writer.Indent--;
			writer.WriteLine("};");
			writer.Indent--;
			writer.Indent--;

			writer.Flush();
			file.Close();
		}

		public string MetadataToCode(MetadataDictionary metadata)
		{
			StringBuilder sb = new StringBuilder();

			sb.AppendLine();

			foreach (var kvp in metadata._entries)
			{
				int idx = kvp.Key;
				MetadataEntry entry = kvp.Value;

				sb.Append($"metadata[{idx}] = new ");
				switch (entry.Identifier)
				{
					case 0:
					{
						var e = (MetadataByte) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
					case 1:
					{
						var e = (MetadataShort) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
					case 2:
					{
						var e = (MetadataInt) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
					case 3:
					{
						var e = (MetadataFloat) entry;
						sb.Append($"{e.GetType().Name}({e.Value.ToString(NumberFormatInfo.InvariantInfo)}f);");
						break;
					}
					case 4:
					{
						var e = (MetadataString) entry;
						sb.Append($"{e.GetType().Name}(\"{e.Value}\");");
						break;
					}
					case 5:
					{
						var e = (MetadataSlot) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
					case 6:
					{
						var e = (MetadataIntCoordinates) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
					case 7:
					{
						var e = (MetadataLong) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						if (idx == 0)
						{
							sb.Append($" // {Convert.ToString((long) e.Value, 2)}; {FlagsToString(e.Value)}");
						}
						break;
					}
					case 8:
					{
						var e = (MetadataVector3) entry;
						sb.Append($"{e.GetType().Name}({e.Value});");
						break;
					}
				}
				sb.Append($" // {(Entity.MetadataFlags) idx}");
				sb.AppendLine();
			}

			return sb.ToString();
		}

		private static string FlagsToString(long input)
		{
			BitArray bits = new BitArray(BitConverter.GetBytes(input));

			byte[] bytes = new byte[8];
			bits.CopyTo(bytes, 0);

			List<Entity.DataFlags> flags = new List<Entity.DataFlags>();
			foreach (var val in Enum.GetValues(typeof(Entity.DataFlags)))
			{
				if (bits[(int) val]) flags.Add((Entity.DataFlags) val);
			}

			StringBuilder sb = new StringBuilder();
			sb.Append(string.Join(", ", flags));
			sb.Append("; ");
			for (var i = 0; i < bits.Count; i++)
			{
				if (bits[i]) sb.Append($"{i}, ");
			}

			return sb.ToString();
		}

		public string CodeName(string name, bool firstUpper = false)
		{
			bool upperCase = firstUpper;

			var result = string.Empty;
			for (int i = 0; i < name.Length; i++)
			{
				if (name[i] == ' ' || name[i] == '_')
				{
					upperCase = true;
				}
				else
				{
					if ((i == 0 && firstUpper) || upperCase)
					{
						result += name[i].ToString().ToUpperInvariant();
						upperCase = false;
					}
					else
					{
						result += name[i];
					}
				}
			}

			result = result.Replace(@"[]", "s");
			return result;
		}

		private int _numberOfChunks = 0;

		public ConcurrentDictionary<ChunkCoordinates, ChunkColumn> Chunks { get; } = new ConcurrentDictionary<ChunkCoordinates, ChunkColumn>();
		public IndentedTextWriter _mobWriter;

		private void SendData(byte[] data, IPEndPoint targetEndpoint)
		{
			if (Connection == null) return;

			try
			{
				Connection.SendData(data, targetEndpoint);
			}
			catch (Exception e)
			{
				Log.Debug("Send exception", e);
			}
		}

		public void SendUnconnectedPing()
		{
			var packet = new UnconnectedPing
			{
				pingId = Stopwatch.GetTimestamp() /*incoming.pingId*/,
				guid = _clientGuid
			};

			var data = packet.Encode();

			if (ServerEndPoint != null)
			{
				SendData(data, ServerEndPoint);
			}
			else
			{
				SendData(data, new IPEndPoint(IPAddress.Broadcast, 19132));
			}
		}

		public void SendConnectedPing()
		{
			var packet = new ConnectedPing() {sendpingtime = DateTime.UtcNow.Ticks};

			SendPacket(packet);
		}

		public void SendConnectedPong(long sendpingtime)
		{
			var packet = new ConnectedPong
			{
				sendpingtime = sendpingtime,
				sendpongtime = sendpingtime + 200
			};

			SendPacket(packet);
		}

		public void SendOpenConnectionRequest1()
		{
			Connection.TryConnect(ServerEndPoint, 1);
		}

		public void SendPacket(Packet packet)
		{
			Session?.SendPacket(packet);
		}

		public void SendChat(string text)
		{
			var packet = McpeText.CreateObject();
			packet.type = (byte) MessageType.Chat;
			packet.source = Username;
			packet.message = text;

			SendPacket(packet);
		}

		public void SendMcpeMovePlayer()
		{
			if (CurrentLocation == null) return;

			if (CurrentLocation.Y < 0)
				CurrentLocation.Y = 64f;

			var movePlayerPacket = McpeMovePlayer.CreateObject();
			movePlayerPacket.runtimeEntityId = EntityId;
			movePlayerPacket.x = CurrentLocation.X;
			movePlayerPacket.y = CurrentLocation.Y;
			movePlayerPacket.z = CurrentLocation.Z;
			movePlayerPacket.yaw = CurrentLocation.Yaw;
			movePlayerPacket.pitch = CurrentLocation.Pitch;
			movePlayerPacket.headYaw = CurrentLocation.HeadYaw;
			movePlayerPacket.mode = 1;
			movePlayerPacket.onGround = false;

			SendPacket(movePlayerPacket);
		}

		public async Task SendCurrentPlayerPositionAsync()
		{
			if (CurrentLocation == null) return;

			if (CurrentLocation.Y < 0) CurrentLocation.Y = 64f;

			var movePlayerPacket = McpeMovePlayer.CreateObject();
			movePlayerPacket.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
			movePlayerPacket.runtimeEntityId = EntityId;
			movePlayerPacket.x = CurrentLocation.X;
			movePlayerPacket.y = CurrentLocation.Y;
			movePlayerPacket.z = CurrentLocation.Z;
			movePlayerPacket.yaw = CurrentLocation.Yaw;
			movePlayerPacket.pitch = CurrentLocation.Pitch;
			movePlayerPacket.headYaw = CurrentLocation.HeadYaw;
			movePlayerPacket.mode = 1;
			movePlayerPacket.onGround = false;

			if (Connection.ConnectionInfo.IsEmulator)
			{
				//var batch = McpeWrapper.CreateObject();
				//batch.ReliabilityHeader.Reliability = Reliability.ReliableOrdered;
				//batch.payload = Compression.CompressPacketsForWrapper(new List<Packet> {movePlayerPacket});

				//Packet message = null;
				//if (Session.CustomMessageHandler != null) message = Session.CustomMessageHandler.HandleOrderedSend(batch);

				//Reliability reliability = message.ReliabilityHeader.Reliability;
				//if (reliability == Reliability.Undefined) reliability = Reliability.Reliable; // Questionable practice

				//if (reliability == Reliability.ReliableOrdered) message.ReliabilityHeader.OrderingIndex = Interlocked.Increment(ref Session.OrderingIndex);
				//await Session.SendPacketAsync(message);

				Session.SendPacket(movePlayerPacket);
				await Session.SendQueueAsync(0);
			}
			else
			{
				Session.SendPacket(movePlayerPacket);
			}
		}


		public void SendDisconnectionNotification()
		{
			SendPacket(new DisconnectionNotification());
		}
	}
}