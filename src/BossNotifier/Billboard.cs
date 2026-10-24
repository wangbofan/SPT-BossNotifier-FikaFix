using UnityEngine;
using Object = UnityEngine.Object;

namespace BossNotifier;

public class Billboard : MonoBehaviour
{
	private Camera _mainCamera;

	private void Start()
	{
		_mainCamera = Camera.main;
	}

	private void LateUpdate()
	{
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_mainCamera != (Object)null)
		{
			((Component)this).transform.rotation = ((Component)_mainCamera).transform.rotation;
		}
	}
}
