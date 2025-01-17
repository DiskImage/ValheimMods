﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulleys
{
    public class MoveableBaseSync: MonoBehaviour
    {
		 
		public MoveableBaseRoot m_baseRoot;

		public Rigidbody m_rigidbody;

		public ZNetView m_nview;
		public bool m_follower;
		public GameObject m_baseRootObject;
		private bool activatedPendingPieces = false;
        internal Pulley m_pulley;
        private ZDOID parentZDOID;

        public void Awake()
        {
			m_nview = GetComponent<ZNetView>(); 
		}

		public void Start()
        {
			if(!m_nview || !m_nview.IsValid())
            {
				return;
            }
			m_pulley = gameObject.AddComponent<Pulley>();
			m_pulley.m_baseRoot = m_baseRoot;
			
			parentZDOID = m_nview.GetZDO().GetZDOID(MoveableBaseRoot.MBParentHash);
            if (parentZDOID != ZDOID.None)
            {
#if DEBUG
                Jotunn.Logger.LogWarning(m_nview.m_zdo.m_uid + " Pulley part of existing base, setting as follower of " + parentZDOID);

#endif
				this.m_follower = true; 
            }
			if(!m_follower)
            {
#if DEBUG
				Jotunn.Logger.LogInfo(m_nview.m_zdo.m_uid + " Creating MoveableBaseRoot");
#endif
				m_baseRootObject = new GameObject
				{
					name = "MoveableBase",
					layer = 0
				};
				m_baseRootObject.transform.SetParent(ZNetScene.instance.m_netSceneRoot.transform);
				m_baseRootObject.transform.position = base.transform.position;
				m_baseRootObject.transform.rotation = base.transform.rotation;
				transform.SetParent(m_baseRootObject.transform);
				m_rigidbody = m_baseRootObject.AddComponent<Rigidbody>();
				m_rigidbody.mass = 1000f;
				m_rigidbody.constraints = RigidbodyConstraints.FreezeRotation & RigidbodyConstraints.FreezePositionX & RigidbodyConstraints.FreezePositionZ;
				m_rigidbody.useGravity = false;
				m_rigidbody.isKinematic = true;
				m_baseRoot = m_baseRootObject.AddComponent<MoveableBaseRoot>();
				activatedPendingPieces = m_baseRoot.ActivatePendingPieces();
				m_baseRoot.m_moveableBaseSync = this;
				m_baseRoot.m_nview = m_nview;
				m_baseRoot.m_id = m_nview.m_zdo.m_uid; 
			}
#if DEBUG
			else
            {
				Jotunn.Logger.LogDebug(m_nview.m_zdo.m_uid + " Attached to existing MoveableBaseRoot");
            }
#endif 
			m_baseRoot?.AddPulley(m_pulley);
		}

		public void Update()
        {
			if (!m_nview || !m_nview.IsValid())
			{
				return;
			}
			if(!m_baseRoot && parentZDOID != ZDOID.None)
            {
                m_baseRootObject = ZNetScene.instance.FindInstance(parentZDOID);
				m_baseRoot = m_baseRootObject?.GetComponentInParent<MoveableBaseRoot>();
            }
			if(m_follower)
            {
				return;
            }
			if (m_baseRoot && !activatedPendingPieces)
            {
				activatedPendingPieces = m_baseRoot.ActivatePendingPieces();
            }
        }

		public void OnDestroy()
		{
			if(m_follower)
            {
				return;
            }
			if (m_baseRoot)
			{
				if(m_baseRoot.OnBaseRootDestroy(this))
                {
#if DEBUG
					Jotunn.Logger.LogWarning(m_nview?.m_zdo?.m_uid + "Destroying MoveableBaseRoot: " + m_baseRoot);
#endif
					Destroy(m_baseRoot.gameObject);
                }
			}
		}

        internal ZDOID GetZDOID()
        {
			return m_nview.m_zdo.m_uid;
        }

        internal void TakeOwnership(MoveableBaseSync destroyingSync)
        { 
			m_follower = false;
			m_rigidbody = destroyingSync.m_rigidbody;
			m_baseRootObject = destroyingSync.m_baseRootObject;
			m_baseRoot = destroyingSync.m_baseRoot;
			m_baseRoot.m_nview = m_nview;
			m_baseRoot.OnOwnershipTransferred();
		}

        internal void SetMoveableBaseRoot(MoveableBaseRoot moveableBaseRoot)
        {
			m_follower = true;
			m_baseRoot = moveableBaseRoot;
			m_baseRootObject = moveableBaseRoot.gameObject;
		}
    }
}