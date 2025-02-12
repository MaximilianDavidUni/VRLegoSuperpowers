using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Valve.VR.InteractionSystem;


/*
 *  An object that is meant to be placed into a Grid
 *  
 *  Represents a brick
 */
public class PlacedObject : MonoBehaviour
{
    public PlacedObjectTypeSO placedObjectTypeSO;
    public Throwable throwable;
    public Interactable interactable;

    private Vector3 worldPosition;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Vector2Int origin;
    private PlacedObjectTypeSO.Dir dir;


    public bool neverPickedUp = true;


    public BrickLineRenderer attachedBrickLineRenderer;
    public Ghost assignedGhost;
    

    [SerializeField] private List<Vector2Int> occupiedGridPositions = new List<Vector2Int>();
    [SerializeField] private HashSet<PlacedObject> connectedToUpwards;
    [SerializeField] private HashSet<PlacedObject> connectedToDownwards;
    [SerializeField] private int gridNumber = -1;

    [SerializeField] private bool showAnchors = false;

    private MeshRenderer meshRenderer;

    private bool pickedUp = false;
    private bool isPlacedInGrid = false;
    private bool hasBaseSupport = false;

    private float lineWidth = 0.001f;

    private Rigidbody rigidbody;

    public Material material;
    
    private GameObject anchor;
    private GameObject frontLeftAnchor;
    private GameObject backLeftAnchor;
    private GameObject backRightAnchor;
    private GameObject visualBrick;





    /*
     *  Creates a new placedObject
     */
    public static PlacedObject Create(
        Vector3 worldPosition, 
        Vector2Int origin, 
        PlacedObjectTypeSO.Dir dir, 
        PlacedObjectTypeSO placedObjectTypeSO, 
        float scale)
    {
        Transform placedObjectTransform = 
            Instantiate(
                placedObjectTypeSO.prefab, 
                worldPosition, 
                Quaternion.Euler(0, placedObjectTypeSO.GetRotationAngle(dir), 0));
        placedObjectTransform.localScale = new Vector3(scale, scale, scale);

        PlacedObject placedObject = placedObjectTransform.GetComponent<PlacedObject>();

        placedObject.gameObject.layer = 12;
        placedObject.placedObjectTypeSO = placedObjectTypeSO;
        placedObject.origin = origin;
        placedObject.dir = dir;
        placedObject.worldPosition = worldPosition;

        return placedObject;
    }




    public void Start()
    {
        // Setup fields
        throwable = GetComponent<Throwable>();
        interactable = GetComponent<Interactable>();
        if (!meshRenderer)
            meshRenderer = gameObject.GetComponentInChildren<MeshRenderer>();
        material = meshRenderer.material;

        // Connect Signals
        throwable.onPickUp.AddListener(this.onPickup);
        throwable.onPickUp.AddListener(delegate { GridBuildingSystemVR.Instance.pickupBrick(this);});
        throwable.onDetachFromHand.AddListener(this.onDropped);
        throwable.onDetachFromHand.AddListener(delegate { GridBuildingSystemVR.Instance.releaseBrick(this); });

        // Setup connection Hashes
        connectedToDownwards = new HashSet<PlacedObject>();
        connectedToUpwards = new HashSet<PlacedObject>();

        // Setup all anchors of the brick
        anchor = transform.GetChild(0).gameObject.transform.GetChild(1).gameObject;
        frontLeftAnchor = transform.GetChild(0).gameObject.transform.GetChild(2).gameObject;
        backLeftAnchor = transform.GetChild(0).gameObject.transform.GetChild(3).gameObject;
        backRightAnchor = transform.GetChild(0).gameObject.transform.GetChild(4).gameObject;
        visualBrick = transform.GetChild(0).gameObject.transform.GetChild(0).gameObject;

        if(!showAnchors)
        {
            anchor.SetActive(false);
            frontLeftAnchor.SetActive(false);
            backLeftAnchor.SetActive(false);
            backRightAnchor.SetActive(false);
        }

        initialPosition = transform.position;

        rigidbody = GetComponent<Rigidbody>();

        makeKinematic();
    }



    public void onPickup()
    {
        makeKinematic();
        // Duplicate Brick if removed from initial position
        if (neverPickedUp)
        {
            duplicateBrick();
            neverPickedUp = false;
        }
    }



    public void onDropped()
    {
        // Parent Brick to Bricks GameObject
        transform.parent = GridBuildingSystemVR.Instance.brickParent.transform;
    }



    /*
     *  Detaches the object from the hand it's held by
     */
    public void forceDetach()
    {
        Debug.Log("In Force Detach");
        transform.parent = null;
        //transform.position = initialPosition;

        Debug.Log(interactable.attachedToHand);
    }



    /*
     *  Instantiates a copy of this brick at its initial position
     */
    private void duplicateBrick()
    {
        // Create clone
        GameObject instance = Instantiate(gameObject, initialPosition, initialRotation);

        // Register clone in Brick list
        GridBuildingSystemVR.Instance.addToBrickList(instance.GetComponent<PlacedObject>());

        // Parent instantiated Brick to Bricks GameObject
        instance.transform.parent = GridBuildingSystemVR.Instance.brickParent.transform;
    }






    private void LateUpdate()
    {
        if (!hasBaseSupport && !pickedUp && rigidbody.isKinematic && !neverPickedUp)
            makePhysicsEnabled();


        if (!pickedUp)
            return;

        try
        {
            // Draw Lines
            Vector3 hitPoint = getRaycastWithPlate();
            attachedBrickLineRenderer.drawAnchorLines(hitPoint);
            attachedBrickLineRenderer.Activate();

            // Update Ghost
            assignedGhost.UpdateGhost();
            assignedGhost.Activate();
        }
        catch (Exception e)
        {
            Debug.LogError(e.Message);
            attachedBrickLineRenderer.Deactivate();

            // Deactivate Ghost
            assignedGhost.Deactivate();
        }
    }









    /*
     *  Returns the intersection point of a ray cast down from the brick towards the plate
     *  
     * 
     *  THROWS EXCEPTION IF NO INTESECTION FOUND
     */
    public Vector3 getRaycastWithPlate()
    {
        // Get main anchor
        GameObject anchor = GetAnchorForCurrentRotation();


        // Setup LayerMask
        RaycastHit hit;
        LayerMask previewMask = LayerMask.GetMask("GridBuildingSystem", "Brick");


        // Cast Ray to buildplate
        Physics.queriesHitBackfaces = true;
        if (anchor.transform.position.y < GridBuildingSystemVR.Instance.parentTransform.position.y)
            Physics.Raycast(anchor.transform.position, Vector3.up, out hit, 999f, previewMask);
        else
            Physics.Raycast(anchor.transform.position, Vector3.down, out hit, 999f, previewMask);
        Physics.queriesHitBackfaces = false;

        Debug.DrawRay(anchor.transform.position, Vector3.down, Color.green);

        if (!hit.collider)
            throw new GridBuildingSystemVR.NoIntersectionException("No intersection with baseplate!");

        return hit.point;
    }









    /*
     *  Returns all positions the brick occupies within the grid
     */
    public List<Vector2Int> GetGridPositionList()
    {
        return placedObjectTypeSO.GetGridPositionList(origin, dir);
    }






    /*
     *  Returns the direction, closest to the current rotation
     *  of the object compared to the baseplate
     */
    public PlacedObjectTypeSO.Dir GetClosestDir()
    {
        // Construct directional angle depending on the base plate's rotation
        float angleBetweenBrickAndPlate = 
            transform.eulerAngles.y + GridBuildingSystemVR.Instance.currentGlobalRotation;


        float[] difs = {
            Mathf.Abs(Mathf.DeltaAngle(angleBetweenBrickAndPlate, 0)),
            Mathf.Abs(Mathf.DeltaAngle(angleBetweenBrickAndPlate, 90)),
            Mathf.Abs(Mathf.DeltaAngle(angleBetweenBrickAndPlate, 180)),
            Mathf.Abs(Mathf.DeltaAngle(angleBetweenBrickAndPlate, 270))
            };


        float min = difs.Min();

        int index = -1;

        for(int i = 0; i < difs.Length; i++)
        {
            if(difs[i] == min)
            {
                index = i;
                break;
            }    
        }

        switch(index)
        {
            case 0:
                //Debug.Log("DOWN");
                return PlacedObjectTypeSO.Dir.Down;
            case 1:
                //Debug.Log("LEFT");
                return PlacedObjectTypeSO.Dir.Left;
            case 2:
                //Debug.Log("UP");
                return PlacedObjectTypeSO.Dir.Up;
            case 3:
                //Debug.Log("RIGHT");
                return PlacedObjectTypeSO.Dir.Right;
            default:
                //Debug.Log("No Angle Found!");
                break;
        }

        return PlacedObjectTypeSO.Dir.Down;
    }




    /*
     *  Returns the anchor that lines up with the grid anchors for its current rotation
     */
    public GameObject GetAnchorForCurrentRotation()
    {
        PlacedObjectTypeSO.Dir dir = this.GetClosestDir();


        GameObject anchor;
        // Set anchor according to rotation
        switch (dir)
        {
            // Brick rotated by 90�
            case PlacedObjectTypeSO.Dir.Left:
                anchor = this.BackRightAnchor;
                break;

            // Brick rotated by 180�
            case PlacedObjectTypeSO.Dir.Up:
                anchor = this.BackLeftAnchor;
                break;

            // Brick rotated by 270�
            case PlacedObjectTypeSO.Dir.Right:
                anchor = this.FrontLeftAnchor;
                break;

            // Brick not rotated
            case PlacedObjectTypeSO.Dir.Down:
            default:
                anchor = this.Anchor;
                break;
        }

        return anchor;
    }





    /*
     *  Changes the brick's material to the given one
     */
    public void changeBrickMaterial(Material material)
    {
        // Get initiate mehsRenderer if not yet done
        if (!meshRenderer)
            meshRenderer = gameObject.GetComponentInChildren<MeshRenderer>();

        meshRenderer.material = material;
        this.material = material;
    }






    /*
     *  Resets the object to its starting position
     *  and orientation
     */
    public void RevertToStartingPosition()
    {
        transform.position = initialPosition;
        transform.rotation = initialRotation;
    }





    public void DestroySelf()
    {
        Destroy(gameObject);
    }







    public void pickUp()
    {
        pickedUp = true;
    }






    public void putDown()
    {
        pickedUp = false;
    }




    public void AddToDownwardConnections(PlacedObject placedObject)
    {
        connectedToDownwards.Add(placedObject);
    }

    public void RemoveFromDownwardConnections(PlacedObject placedObject)
    {
        connectedToDownwards.Remove(placedObject);
    }

    public void ClearDownwardConnections()
    {
        connectedToDownwards.Clear();
    }


    public HashSet<PlacedObject> DownwardConnections
    {
        get { return connectedToDownwards; }
        set { connectedToDownwards = value; }
    }



    public GameObject Anchor
    {
        get { return anchor; }
    }


    public GameObject FrontLeftAnchor
    {
        get { return frontLeftAnchor; }
    }

    public GameObject BackLeftAnchor
    {
        get { return backLeftAnchor; }
    }


    public GameObject BackRightAnchor
    {
        get { return backRightAnchor; }
    }


    public GameObject VisualBrick
    {
        get { return visualBrick; }
    }


    public void AddToUpwardConnections(PlacedObject placedObject)
    {
        connectedToUpwards.Add(placedObject);
    }



    public void RemoveFromUpwardConnections(PlacedObject placedObject)
    {
        connectedToUpwards.Remove(placedObject);
    }



    public void ClearUpwardConnections()
    {
        connectedToUpwards.Clear();
    }


    public HashSet<PlacedObject> UpwardConnections
    {
        get { return connectedToUpwards; }
        set { connectedToUpwards = value; }
    }


    public void SetOrigin(Vector2Int value)
    {
        origin = value;
    }

    public void SetDir(PlacedObjectTypeSO.Dir value)
    {
        dir = value;
    }

    public void makeKinematic()
    {
        Debug.Log("Making " + this.ToString() + " Kinematic!");
        rigidbody.isKinematic = true;
    }

    public void makePhysicsEnabled()
    {
        Debug.Log("Making " + this.ToString() + " Physics enabled!");
        rigidbody.isKinematic = false;
    }


    public void ignoreCollisions(bool ignore = true)
    {
        if(ignore)
        {
            MyUtilities.MyUtils.SetLayerRecursively(this.gameObject, 13);
            Debug.Log("Making " + this.ToString() + "Ignore Collisions!");
        }
        else
        {
            MyUtilities.MyUtils.SetLayerRecursively(this.gameObject, 0);
            Debug.Log("Making " + this.ToString() + "Receive Collisions!");
        }
    }



    public bool HasBaseSupport()
    {
        return hasBaseSupport;
    }

    public void SetBaseSupport(bool value)
    {
        hasBaseSupport = value;
    }

    public int GetGridNumber()
    {
        return gridNumber;
    }


    public void SetGridNumber(int number)
    {
        gridNumber = number;
    }


    public List<Vector2Int> OccupiedGridPositions
    {
        get { return occupiedGridPositions; }
        set { occupiedGridPositions = value; }
    }


    public bool IsPlacedInGrid
    {
        get { return isPlacedInGrid; }
        set { isPlacedInGrid = value; }
    }


    public bool isPickedUp()
    {
        return pickedUp;
    }
}
