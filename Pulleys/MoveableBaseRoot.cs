﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pulleys
{
	public class MoveableBaseRoot : Ship
	{
		public static readonly KeyValuePair<int, int> MBParentHash = ZDO.GetHashZDOID("marcopogo.MBParent"); 
		public static readonly int MBPositionHash = "marcopogo.MBPosition".GetStableHashCode(); 
		public static readonly int MBRotationHash = "marcopogo.MBRotation".GetStableHashCode();

		public static Dictionary<ZDOID, List<Piece>> m_pendingPieces = new Dictionary<ZDOID, List<Piece>>();

		public MoveableBaseSync m_moveableBaseSync;
		public readonly List<MoveableBaseSync> m_followers = new List<MoveableBaseSync>();    
		public readonly List<Pulley> m_pulleys = new List<Pulley>();

		public readonly List<Piece> m_pieces = new List<Piece>();

        // public Rigidbody m_syncRigidbody;

        //public List<RudderComponent> m_rudderPieces = new List<RudderComponent>();

        public readonly List<Piece> m_portals = new List<Piece>();


		public Vector2i m_sector;

		//public Bounds m_bounds;

		//public BoxCollider m_blockingcollider;
		  
		//public BoxCollider m_onboardcollider;

		public ZDOID m_id;

		public float m_lastPortalUpdate;

		public bool m_statsOverride;

		private float highestFloor;
        private int m_supportRayMask;

        public new void Awake()
		{ 
			m_supportRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
			Heightmap.GetHeight(transform.position, out highestFloor);
			m_body = GetComponent<Rigidbody>();
			m_shipControlls = GetComponentInChildren<PulleyControlls>();
			InvokeRepeating("UpdateHeightMap", 10f, 10f); 
		}

		public void UpdateHeightMap()
		{ 
			Heightmap.GetHeight(transform.position, out highestFloor);
			foreach (Piece piece in m_pieces)
			{
				if(Physics.Raycast(piece.transform.position, piece.transform.up * -1f,   out var hitInfo, 2000f, m_supportRayMask))
                {
					highestFloor = Math.Max(hitInfo.transform.position.y, highestFloor);
				} else
                {
					if (Heightmap.GetHeight(piece.transform.position, out float floorHeight))
					{
						highestFloor = Math.Max(floorHeight, highestFloor);
					}
                }
			}

#if DEBUG
			Jotunn.Logger.LogInfo("Updated max floor height to: " + highestFloor);
#endif
		}

		internal void AddPulley(Pulley pulley)
        {
#if DEBUG
			Jotunn.Logger.LogInfo(GetZDOID() + " AddPulley(" + pulley.m_nview.m_zdo.m_uid + ")");
#endif
			m_pulleys.Add(pulley);
			pulley.m_pulleyControlls.m_ship = this;
			pulley.m_pulleyControlls.m_baseRoot = this;
			pulley.m_baseRoot = this;
			if (!m_shipControlls)
            {
				SetActiveControll(pulley.m_pulleyControlls);
            }
        }

        public bool OnBaseRootDestroy(MoveableBaseSync destroyingSync)
		{
            MoveableBaseSync connectedFollower = m_followers.Find(follower => follower.m_pulley.IsConnected());
			if(connectedFollower)
            {
				m_followers.Remove(connectedFollower);
				connectedFollower.TakeOwnership(destroyingSync);
				return false;
            }

			if (!ZNetScene.instance || !ZNetScene.instance.m_netSceneRoot || !(m_id != ZDOID.None))
			{
				return true;
			}
			for (int i = 0; i < m_pieces.Count; i++)
			{
				Piece piece = m_pieces[i];
				if ((bool)piece)
				{ 
					AddInactivePiece(m_id, piece);
				}
			}
			List<Player> allPlayers = Player.GetAllPlayers();
			for (int j = 0; j < allPlayers.Count; j++)
			{
				if (allPlayers[j] && allPlayers[j].transform.parent == transform)
				{
					allPlayers[j].transform.SetParent(ZNetScene.instance.m_netSceneRoot.transform);
				}
			}
			return true;
		}

        internal void OnOwnershipTransferred()
        {
#if DEBUG

#endif
			foreach(Piece piece in m_pieces)
            {
				piece.m_nview.m_zdo.Set(MBParentHash, m_nview.m_zdo.m_uid);
            }
			//Maybe?
			m_nview.Register("Stop", RPC_Stop);
			m_nview.Register("Forward", RPC_Forward);
			m_nview.Register("Backward", RPC_Backward);
			m_nview.Register<float>("Rudder", RPC_Rudder);
		}

        internal void RemovePulley(Pulley m_pulley)
        {
			m_pulleys.Remove(m_pulley);
			if(m_shipControlls == m_pulley.m_pulleyControlls)
            {
				if(m_pulleys.Count == 0)
                {
#if DEBUG
					Jotunn.Logger.LogWarning("Last pulley removed, destroying MoveableBaseRoot");
#endif
					Object.Destroy(gameObject);
					return;
                }
#if DEBUG
                Jotunn.Logger.LogWarning("Active pulley controlls removed, selecting random remaining as active");
#endif
				SetActiveControll(m_pulleys.First().m_pulleyControlls);
            }
        }

        internal void SetActiveControll(PulleyControlls pulleyControlls)
        {
#if DEBUG
            Jotunn.Logger.LogInfo(GetZDOID() + " Setting active control: " + pulleyControlls.m_nview.m_zdo.m_uid);
#endif
            m_shipControlls = pulleyControlls;
            m_controlGuiPos = pulleyControlls.m_pulley.m_controlGuiPos;
        }

        private ZDOID GetZDOID()
        {
            return m_nview.m_zdo.m_uid;
        }

        public void LateUpdate()
		{ 
			Vector2i zone = ZoneSystem.instance.GetZone(transform.position);
			if (zone != m_sector)
			{
				m_sector = zone;
				UpdateAllPieces();
			}
			else
			{
				UpdatePortals();
			}
		}

		public void UpdatePortals()
		{
			if (!(Time.time - m_lastPortalUpdate > 0.5f))
			{
				return;
			}
			m_lastPortalUpdate = Time.time;
			for (int i = 0; i < m_portals.Count; i++)
			{
				Piece piece = m_portals[i];
				if (!piece || !piece.m_nview || piece.m_nview.m_zdo == null)
				{
					m_pieces.RemoveAt(i);
					i--;
					continue;
				}
				Vector3 position = piece.m_nview.m_zdo.GetPosition();
				if ((piece.transform.position - position).sqrMagnitude > 1f)
				{
					piece.m_nview.m_zdo.SetPosition(piece.transform.position);
				}
			}
		}

		public void UpdateAllPieces()
		{
			for (int i = 0; i < m_pieces.Count; i++)
			{
				Piece piece = m_pieces[i];
				if (!piece)
				{
					m_pieces.RemoveAt(i);
					i--;
					continue;
				}
				ZNetView component = piece.GetComponent<ZNetView>();
				if ((bool)component)
				{
					component.m_zdo.SetPosition(piece.transform.position);
				}
			}
		}

		public static void AddInactivePiece(ZDOID id, Piece piece)
		{
#if DEBUG
            Jotunn.Logger.LogInfo("Adding inactive piece: " + id + " " + piece + " (" + piece.m_nview?.m_zdo.m_uid + ")");
#endif
			if (!m_pendingPieces.TryGetValue(id, out var value))
			{
				value = new List<Piece>();
				m_pendingPieces.Add(id, value);
			}
			value.Add(piece);
			WearNTear component = piece.GetComponent<WearNTear>();
			if ((bool)component)
			{
				component.enabled = false;
			}
		}

		public void RemovePiece(Piece piece)
		{
			m_pieces.Remove(piece);
			//MastComponent component = piece.GetComponent<MastComponent>();
			//if ((bool)component)
			//{
			//	m_mastPieces.Remove(component);
			//}
			//RudderComponent component2 = piece.GetComponent<RudderComponent>();
			//if ((bool)component2)
			//{
			//	m_rudderPieces.Remove(component2);
			//}
			TeleportWorld component3 = piece.GetComponent<TeleportWorld>();
			if ((bool)component3)
			{
				m_portals.Remove(piece);
			}
			UpdateStats();
		}

		public void UpdateStats()
		{
			 
		}

		public bool ActivatePendingPieces()
		{
			if (!m_nview || m_nview.m_zdo == null)
			{
				return false;
			}
#if DEBUG
			Jotunn.Logger.LogInfo("Activate pending pieces for " + m_nview.m_zdo.m_uid);
#endif
			ZDOID uid = m_nview.m_zdo.m_uid;
			if (!m_pendingPieces.TryGetValue(uid, out var value))
			{
				return true;
			}
			for (int i = 0; i < value.Count; i++)
			{
				Piece piece = value[i];
				if ((bool)piece)
				{
					ActivatePiece(piece);
				}
			}
			value.Clear();
			m_pendingPieces.Remove(uid);
			return true;
		}

		public static void InitPiece(Piece piece)
		{
			Rigidbody componentInChildren = piece.GetComponentInChildren<Rigidbody>();
			if (componentInChildren && !componentInChildren.isKinematic)
			{
				Jotunn.Logger.LogInfo("Ignoring rigidbody: " + piece);
				return;
			}
			ZDOID zDOID = piece.m_nview.m_zdo.GetZDOID(MBParentHash);
			if (zDOID == ZDOID.None)
			{ 
				return;
			}
#if DEBUG
			Jotunn.Logger.LogInfo("Piece (" + piece.m_nview.m_zdo.m_uid + ") has Parent: " + zDOID );
#endif
			GameObject gameObject = ZNetScene.instance.FindInstance(zDOID);
			if ((bool)gameObject)
			{
				MoveableBaseSync component = gameObject.GetComponent<MoveableBaseSync>();
				if (component && component.m_baseRoot)
				{
					component.m_baseRoot.ActivatePiece(piece);
				}
			}
			else
			{
				AddInactivePiece(zDOID, piece);
			}
		}

		public void ActivatePiece(Piece piece)
		{
#if DEBUG
			Jotunn.Logger.LogInfo(GetZDOID() + " Activating piece " + piece.m_name + " @ " + piece.transform.position + ": Parent: " + m_nview.m_zdo.m_uid);
#endif
			ZNetView component = piece.GetComponent<ZNetView>();
			if ((bool)component)
			{
				piece.transform.SetParent(transform);
				piece.transform.localPosition = component.m_zdo.GetVec3(MBPositionHash, piece.transform.localPosition);
				piece.transform.localRotation = component.m_zdo.GetQuaternion(MBRotationHash, piece.transform.localRotation);
				WearNTear component2 = piece.GetComponent<WearNTear>();
				if ((bool)component2)
				{
					component2.enabled = true;
				}
				AddPiece(piece);
			}
		}

		public void AddNewPiece(Piece piece)
		{
#if DEBUG
			Jotunn.Logger.LogInfo(GetZDOID() + " Adding piece " + piece.m_name + " @ " + piece.transform.position + ": Parent: " + m_nview.m_zdo.m_uid);
#endif
			piece.transform.SetParent(transform);
			ZNetView component = piece.GetComponent<ZNetView>();

			component.m_zdo.Set(MBParentHash, m_nview.m_zdo.m_uid);
			component.m_zdo.Set(MBPositionHash, piece.transform.localPosition);
			component.m_zdo.Set(MBRotationHash, piece.transform.localRotation);
			AddPiece(piece);
		}

		public void AddPiece(Piece piece)
		{
			m_pieces.Add(piece);
			//EncapsulateBounds(piece);
			//	MastComponent component = piece.GetComponent<MastComponent>();
			//	if ((bool)component)
			//	{
			//		m_mastPieces.Add(component);
			//	}
			//	RudderComponent component2 = piece.GetComponent<RudderComponent>();
			//	if ((bool)component2)
			//	{
			//		if (!component2.m_controls)
			//		{
			//			component2.m_controls = piece.GetComponentInChildren<ShipControlls>();
			//		}
			//		if (!component2.m_wheel)
			//		{
			//			component2.m_wheel = piece.transform.Find("controls/wheel");
			//		}
			//		component2.m_controls.m_nview = m_nview;
			//		component2.m_controls.m_ship = m_moveableBaseSync.GetComponent<Ship>();
			//		m_rudderPieces.Add(component2);
			//	} 
			if(piece.TryGetComponent(out MoveableBaseSync moveableBaseSync))
            {
				AddFollower(moveableBaseSync); 
            }

            if(piece.TryGetComponent(out WearNTear wearNTear))
            {
				wearNTear.m_onDestroyed += () => OnDestroyed(piece);
            }


			TeleportWorld component3 = piece.GetComponent<TeleportWorld>();
			if ((bool)component3)
			{
				m_portals.Add(piece);
			}
			MeshRenderer[] componentsInChildren = piece.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
			MeshRenderer[] array = componentsInChildren;
			foreach (MeshRenderer meshRenderer in array)
			{
				if ((bool)meshRenderer.sharedMaterial)
				{
					Material[] sharedMaterials = meshRenderer.sharedMaterials;
					for (int j = 0; j < sharedMaterials.Length; j++)
					{
						Material material = new Material(sharedMaterials[j]);
						material.SetFloat("_RippleDistance", 0f);
						material.SetFloat("_ValueNoise", 0f);
						sharedMaterials[j] = material;
					}
					meshRenderer.sharedMaterials = sharedMaterials;
				}
			}
			Rigidbody[] componentsInChildren2 = piece.GetComponentsInChildren<Rigidbody>();
			for (int k = 0; k < componentsInChildren2.Length; k++)
			{
				if (componentsInChildren2[k].isKinematic)
				{
#if DEBUG
					Jotunn.Logger.LogWarning(GetZDOID() + " Destroying rigidbody: " + componentsInChildren2[k]);
#endif
					Destroy(componentsInChildren2[k]);
				}
			}
			UpdateStats();
		}

        private void OnDestroyed(Piece piece)
        {
#if DEBUG
			Jotunn.Logger.LogWarning(GetZDOID() + " Removing destroyed piece " + piece + " " + piece.m_nview.m_zdo.m_uid);
#endif
			m_pieces.Remove(piece);
			if(piece.TryGetComponent(out Pulley pulley))
            {
				RemovePulley(pulley);
            }
        }

        private void AddFollower(MoveableBaseSync moveableBaseSync)
        {
#if DEBUG
			Jotunn.Logger.LogInfo(GetZDOID() + " Adding follower: " + moveableBaseSync.GetZDOID());
#endif
			moveableBaseSync.SetMoveableBaseRoot(this);
			if (moveableBaseSync.m_pulley)
			{
				AddPulley(moveableBaseSync.m_pulley);
			}
			m_followers.Add(moveableBaseSync);
		}

        public new void ApplyMovementControlls(Vector3 direction)
		{
			base.ApplyMovementControlls(direction);
		}

		public new void UpdateSail(float dt)
		{
			//Nothing to do
		}

		public new Ship.Speed GetSpeed()
		{
			//Only used by MusicMan
			return Speed.Stop;
		}

		public new void UpdateSailSize(float dt)
		{
			//Nothing to do
		}

		public new void OnTriggerEnter(Collider collider)
		{
			base.OnTriggerEnter(collider);
		}


		public new void OnTriggerExit(Collider collider)
		{
			base.OnTriggerExit(collider);
		}

		public new void UpdateControlls(float dt)
		{
			if(!m_nview || !m_nview.IsValid())
            {
				return;
            }
			if (m_nview.IsOwner())
			{
				m_nview.GetZDO().Set("forward", (int)m_speed);
				return;
			}
			m_speed = (Speed)m_nview.GetZDO().GetInt("forward");
		}

		public float GetShortestRope()
        {
			float shortest = float.MaxValue;
			foreach(Pulley pulley in m_pulleys)
            {
				if(pulley.IsConnected())
                {
					shortest = Math.Min(shortest, pulley.GetRopeLength());
                }
            }
			return shortest;
        }

		public new void FixedUpdate()
		{
			bool haveControllingPlayer = HaveControllingPlayer();
			UpdateControlls(Time.fixedDeltaTime);
			if (m_nview && !m_nview.IsOwner())
			{
				return;
			}
			if (m_players.Count == 0)
			{
				m_speed = Speed.Stop;
			}
			if (!haveControllingPlayer && (m_speed == Speed.Slow || m_speed == Speed.Back))
			{
				m_speed = Speed.Stop;
			}
			if (m_speed == Speed.Stop)
			{
				return;
			}

			float ropeLength = GetShortestRope(); 
			Vector3 positionChange = Vector3.zero;
			switch (m_speed)
			{
				case Speed.Stop:
					return;
				case Speed.Half:
				case Speed.Full:
					m_speed = Speed.Slow;
					goto case Speed.Slow;
				case Speed.Slow:
					float ropeLeftUp = ropeLength - 3f;
					positionChange.y += Math.Min(m_rudderSpeed * Time.fixedDeltaTime, ropeLeftUp);
					break;
				case Speed.Back:
					float ropeLeftDown = transform.position.y - highestFloor;
					positionChange.y -= Math.Min(m_rudderSpeed * Time.fixedDeltaTime, ropeLeftDown);
					break;
			}
			if (!m_body)
			{
				m_body = GetComponentInParent<Rigidbody>();
				if (!m_body)
				{
					Jotunn.Logger.LogWarning("No rigid body!");
					return;
				}
			}
			m_body.MovePosition(transform.position + positionChange);
			UpdateRotation(ropeLength);
		}

        private void UpdateRotation(float ropeLength = -1f)
        {
			foreach (Pulley pulley in m_pulleys)
			{
				if (pulley.IsConnected() || m_shipControlls == pulley.m_pulleyControlls)
				{
					pulley.UpdateRotation(ropeLength);
				}
			}
		}
		 
        //public void EncapsulateBounds(Piece piece)
        //{
        //	List<Collider> allColliders = piece.GetAllColliders();
        //	Door componentInChildren = piece.GetComponentInChildren<Door>();
        //	if (!componentInChildren)
        //	{
        //		m_bounds.Encapsulate(piece.transform.localPosition);
        //	}
        //	for (int i = 0; i < allColliders.Count; i++)
        //	{
        //		Physics.IgnoreCollision(allColliders[i], m_blockingcollider, ignore: true); 
        //		Physics.IgnoreCollision(allColliders[i], m_onboardcollider, ignore: true);
        //	}
        //	//m_blockingcollider.size = new Vector3(m_bounds.size.x, 3f, m_bounds.size.z);
        //	//m_blockingcollider.center = new Vector3(m_bounds.center.x, -0.2f, m_bounds.center.z); 
        //	//m_onboardcollider.size = m_bounds.size;
        //	//m_onboardcollider.center = m_bounds.center;
        //}

        public int GetPieceCount()
		{
			return m_pieces.Count;
		}
	}
}
