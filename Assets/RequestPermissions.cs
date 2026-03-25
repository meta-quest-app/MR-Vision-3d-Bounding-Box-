using UnityEngine;
using UnityEngine.Android;

public class RequestPermissions : MonoBehaviour
{
    void Start()
    {
        // Request scene permission at app launch
        if (!Permission.HasUserAuthorizedPermission(OVRPermissionsRequester.ScenePermission))
        {
            Debug.Log("[Permissions] Requesting scene permission...");
            Permission.RequestUserPermission(OVRPermissionsRequester.ScenePermission);
        }
        else
        {
            Debug.Log("[Permissions] Scene permission already granted.");
        }
    }
}