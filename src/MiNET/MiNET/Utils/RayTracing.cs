using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using MiNET.Blocks;
using MiNET.Worlds;

namespace MiNET.Utils
{
	public class RayTracing
	{
		public Level Level { get; set; }

		public Vector3 Position { get; set; }
		public Vector3 Offset { get; set; }
		public Vector3 Aura { get; set; }

		public RayTracing(Level level, Vector3 position, Vector3 offset) : this(level, position, offset, Vector3.Zero)
		{

		}

		public RayTracing(Level level, Vector3 position, Vector3 offset, Vector3 aura)
		{
			Level = level;
			Position = position;
			Offset = offset;
			Aura = aura;
		}

		private int _stepsCount = 5;
		public bool Next(out Block collidedBlock)
		{
			var absOffset = Vector3.Abs(Offset);
			var maxDir = Math.Max(Math.Max(absOffset.X, absOffset.Y), absOffset.Z);
			var step = Offset / maxDir;

			Position += Next(step, _stepsCount, false, out bool collided, out collidedBlock);
			return collided;
		}

		public bool ToDestination(out Block collidedBlock)
		{
			var step = Offset;

			var absOffset = Vector3.Abs(Offset);
			if (absOffset.X > 1 || absOffset.Y > 1 || absOffset.Z > 1)
			{
				var maxDir = Math.Max(Math.Max(absOffset.X, absOffset.Y), absOffset.Z);
				step /= maxDir;
			}

			int stepsCount = (int)(Offset.Length() / step.Length());

			Position += Next(step, stepsCount, true, out bool collided, out collidedBlock);

			return collided;
		}

		private Vector3 Next(Vector3 step, int stepsCount, bool limited, out bool collided, out Block collidedBlock)
		{
			var offset = GetAdjustedLengthFromCollision(step, stepsCount, out collidedBlock);

			collided = offset != Vector3.Zero;

			if (!collided)
			{
				offset = step * stepsCount;
			}

			if (limited && Offset.Length() < offset.Length())
			{
				collided = false;
				return Offset;
			}

			return offset;
		}

		private Vector3 GetAdjustedLengthFromCollision(Vector3 step, int stepsCount, out Block collidedBlock)
		{
			collidedBlock = null;
			for (int i = 0; i <= stepsCount; i++)
			{
				var distVec = step * i;

				var coordinates = new List<BlockCoordinates>();

				BlockCoordinates xBlockPos = Position + distVec + new Vector3(step.X, 0, 0);
				coordinates.Add(xBlockPos);
				Block xBlock = Level.GetBlock(xBlockPos);
				var xCollision = GetAdjustedLengthFromBBCollision(xBlock);
				var	minCollision = xCollision;
				if (xCollision != Vector3.Zero) collidedBlock = xBlock;

				BlockCoordinates yBlockPos = Position + distVec + new Vector3(0, step.Y, 0);
				if (!coordinates.Contains(yBlockPos))
				{
					coordinates.Add(yBlockPos);
					Block yBlock = Level.GetBlock(yBlockPos);
					var yCollision = GetAdjustedLengthFromBBCollision(yBlock);
					if (minCollision == Vector3.Zero || (yCollision != Vector3.Zero && minCollision.Length() > yCollision.Length()))
					{
						minCollision = yCollision;
						collidedBlock = yBlock;
					}
				}

				BlockCoordinates zBlockPos = Position + distVec + new Vector3(0, 0, step.Z);
				if (!coordinates.Contains(zBlockPos))
				{
					coordinates.Add(zBlockPos);
					Block zBlock = Level.GetBlock(zBlockPos);
					var zCollision = GetAdjustedLengthFromBBCollision(zBlock);
					if (minCollision == Vector3.Zero || (zCollision != Vector3.Zero && minCollision.Length() > zCollision.Length()))
					{
						minCollision = zCollision;
						collidedBlock = zBlock;
					}
				}

				if (minCollision != Vector3.Zero)
				{
					return minCollision;
				}

				BlockCoordinates xyBlockPos = Position + distVec + new Vector3(step.X, step.Y, 0);
				if (!coordinates.Contains(xyBlockPos))
				{
					coordinates.Add(xyBlockPos);
					Block xyBlock = Level.GetBlock(xyBlockPos);
					var xyCollision = GetAdjustedLengthFromBBCollision(xyBlock);
					if (minCollision == Vector3.Zero || (xyCollision != Vector3.Zero && minCollision.Length() > xyCollision.Length()))
					{
						minCollision = xyCollision;
						collidedBlock = xyBlock;
					}
				}

				BlockCoordinates xzBlockPos = Position + distVec + new Vector3(step.X, 0, step.Z);
				if (!coordinates.Contains(xzBlockPos))
				{
					coordinates.Add(xzBlockPos);
					Block xzBlock = Level.GetBlock(xzBlockPos);
					var xzCollision = GetAdjustedLengthFromBBCollision(xzBlock);
					if (minCollision == Vector3.Zero || (xzCollision != Vector3.Zero && minCollision.Length() > xzCollision.Length()))
					{
						minCollision = xzCollision;
						collidedBlock = xzBlock;
					}
				}

				BlockCoordinates yzBlockPos = Position + distVec + new Vector3(0, step.Y, step.Z);
				if (!coordinates.Contains(yzBlockPos))
				{
					coordinates.Add(yzBlockPos);
					Block yzBlock = Level.GetBlock(yzBlockPos);
					var yzCollision = GetAdjustedLengthFromBBCollision(yzBlock);
					if (minCollision == Vector3.Zero || (yzCollision != Vector3.Zero && minCollision.Length() > yzCollision.Length()))
					{
						minCollision = yzCollision;
						collidedBlock = yzBlock;
					}
				}

				if (minCollision != Vector3.Zero)
				{
					return minCollision;
				}

				BlockCoordinates xyzBlockPos = Position + distVec + step;
				if (!coordinates.Contains(xyzBlockPos))
				{
					coordinates.Add(xyzBlockPos);
					Block xyzBlock = Level.GetBlock(xyzBlockPos);
					var xyzCollision = GetAdjustedLengthFromBBCollision(xyzBlock);
					if (minCollision == Vector3.Zero || (xyzCollision != Vector3.Zero && minCollision.Length() > xyzCollision.Length()))
					{
						minCollision = xyzCollision;
						collidedBlock = xyzBlock;
					}
				}

				if (minCollision != Vector3.Zero)
				{
					return minCollision;
				}
			}

			return Vector3.Zero;
		}

		private Vector3 GetAdjustedLengthFromBBCollision(Block block)
		{
			if (block.IsSolid)
			{
				Ray ray = new Ray(Position, Offset.Normalize());
				var distance = ray.Intersects(block.GetBoundingBox());
				if (distance.HasValue)
				{
					return ray.Direction * new Vector3((float) distance) - Aura * (ray.Direction / Vector3.Abs(ray.Direction));
				}
			}

			return Vector3.Zero;
		}
	}
}
