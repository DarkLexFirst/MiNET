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
// All portions of the code written by Niclas Olofsson are Copyright (c) 2014-2018 Niclas Olofsson. 
// All Rights Reserved.

#endregion

using System.Collections.Generic;
using System.Numerics;
using MiNET.Items;
using MiNET.Net;

namespace MiNET.Utils
{
	public abstract class Transaction
	{
		public List<TransactionRecord> TransactionRecords { get; set; } = new List<TransactionRecord>();

		public override string ToString()
		{
			var str = $"TransactionType: {GetType()}\n";
			str += "{\n";
			foreach (var tr in TransactionRecords)
				str += $"	{tr.ToString()}\n\n";
			str += "}";
			return str;
		}
	}

	public class NormalTransaction : Transaction
	{
	}
	public class InventoryMismatchTransaction : Transaction
	{
	}
	public class ItemUseTransaction : Transaction
	{
		public McpeInventoryTransaction.ItemUseAction ActionType { get; set; }
		public BlockCoordinates Position { get; set; }
		public int Face { get; set; }
		public int Slot { get; set; }
		public Item Item { get; set; }
		public Vector3 FromPosition { get; set; }
		public Vector3 ClickPosition { get; set; }
		public uint BlockRuntimeId { get; set; }

		public override string ToString()
		{
			var str = base.ToString() + "\n";
			str += $"ActionType = {ActionType}, Position = {Position}, Face = {Face}, Slot = {Slot}\n";
			str += $"Item = {Item}\n";
			str += $"FromPosition = {FromPosition}, ClickPosition = {ClickPosition}\n";
			str += $"BlockRuntimeId = {BlockRuntimeId}\n";
			return str;
		}
	}
	public class ItemUseOnEntityTransaction : Transaction
	{
		public long EntityId { get; set; }
		public McpeInventoryTransaction.ItemUseOnEntityAction ActionType { get; set; }
		public int Slot { get; set; }
		public Item Item { get; set; }
		public Vector3 FromPosition { get; set; }
		public Vector3 ClickPosition { get; set; }

		public override string ToString()
		{
			var str = base.ToString() + "\n";
			str += $"ActionType = {ActionType}, EntityId = {EntityId}, Slot = {Slot}\n";
			str += $"FromPosition = {FromPosition}, ClickPosition = {ClickPosition}\n";
			str += $"Item = {Item}\n";
			return str;
		}
	}
	public class ItemReleaseTransaction : Transaction
	{
		public McpeInventoryTransaction.ItemReleaseAction ActionType { get; set; }
		public int Slot { get; set; }
		public Item Item { get; set; }
		public Vector3 FromPosition { get; set; }

		public override string ToString()
		{
			var str = base.ToString() + "\n";
			str += $"ActionType = {ActionType}, Slot = {Slot}, FromPosition = {FromPosition}\n";
			str += $"Item = {Item}\n";
			return str;
		}
	}

	public abstract class TransactionRecord
	{
		public int Slot { get; set; }
		public Item OldItem { get; set; }
		public Item NewItem { get; set; }

		public override string ToString()
		{
			return $"RecordType: \"{GetType()}\" <->\n" +
				$"	Slot = {Slot}\n" +
				$"	OldItem = {OldItem}\n" +
				$"	NewItem = {NewItem}\n";
		}
	}

	public class ContainerTransactionRecord : TransactionRecord
	{
		public int InventoryId { get; set; }

		public override string ToString()
		{
			return base.ToString() + "\n" +
				$"	InventoryId = {InventoryId}\n";
		}
	}

	public class GlobalTransactionRecord : TransactionRecord
	{
	}

	public class WorldInteractionTransactionRecord : TransactionRecord
	{
		public int Flags { get; set; } // NoFlag = 0 WorldInteractionRandom = 1

		public override string ToString()
		{
			return base.ToString() + "\n" +
				$"	Flags = {Flags}\n";
		}
}

	public class CreativeTransactionRecord : TransactionRecord
	{
		public int InventoryId { get; set; } = 0x79; // Creative

		public override string ToString()
		{
			return base.ToString() + "\n" +
				$"	InventoryId = {InventoryId}\n";
		}
	}

	public class CraftTransactionRecord : TransactionRecord
	{
		public McpeInventoryTransaction.CraftingAction Action { get; set; }

		public override string ToString()
		{
			return base.ToString() + "\n" +
				$"	Action = {Action}\n";
		}
	}
}