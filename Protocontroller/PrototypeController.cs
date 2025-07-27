using Godot;
using System.Collections.Generic;

[Tool]
public partial class PrototypeController : CharacterBody3D
{
    #region Editor Variables
    [ExportGroup("Movement Settings")]
    [Export] private float _baseSpeed = 5.0f;
    [Export] private float _sprintMultiplier = 1.8f;
    [Export] private float _acceleration = 15.0f;
    [Export] private float _deceleration = 20.0f;
    [Export] private float _airControl = 0.3f;
    [Export] private float _slopeMaxAngle = 40.0f;

    [ExportGroup("Jump Settings")]
    [Export] private float _jumpVelocity = 4.0f;
    [Export] private float _gravityMultiplier = 1f;
    [Export] private float _coyoteTime = 0.15f;
    [Export] private float _jumpBufferTime = 0.1f;

    [ExportGroup("Look Settings")]
    [Export(PropertyHint.Range, "0.01, 2.0")] 
    private float _mouseSensitivity = 0.1f;
    [Export(PropertyHint.Range, "70, 110")] 
    private float _verticalLookLimit = 90.0f;
    [Export] private bool _invertYAxis;

    [ExportGroup("Capabilities")]
    [Export] private bool _enableMovement = true;
    [Export] private bool _enableJump = true;
    [Export] private bool _enableLook = true;
    [Export] private bool _enableSprint = true;
    [Export] private bool _enableCrouch = true;
    [Export] private bool _enableNoclip = true;

    [ExportGroup("Input Actions")]
    [Export] private StringName _moveForwardAction = "move_forward";
    [Export] private StringName _moveBackwardAction = "move_backward";
    [Export] private StringName _moveLeftAction = "move_left";
    [Export] private StringName _moveRightAction = "move_right";
    [Export] private StringName _jumpAction = "move_jump";
    [Export] private StringName _sprintAction = "move_sprint";
    [Export] private StringName _crouchAction = "move_crouch";
    [Export] private StringName _noclipAction = "move_noclip";
    #endregion

    #region Node References
    private Camera3D _playerCamera;
    private Node3D _cameraPivot;
    private CollisionShape3D _collisionShape;
    #endregion

    #region Noclip needed var
    private uint _originalCollisionLayer;
    private uint _originalCollisionMask;
    private float _originalGravityMultiplier;
    #endregion


    #region Runtime State
    private float _verticalLookRotation;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _isJumping;
    private bool _isSprinting;
    private bool _isCrouching;
    private bool _isNoclip;
    private float _originalHeight;
    private Vector3 _originalPivotPosition;
    private float _currentHeight;
    private Vector3 _currentPivotPosition;
    private Vector3 _meshOriginalPosition;
    #endregion

    #region Input Validation
    private readonly Dictionary<StringName, bool> _actionStates = new();
    private static readonly StringName[] RequiredActions =
    [
        "move_forward", "move_backward", "move_left", "move_right", "move_jump"
    ];

    public override void _Ready()
    {
        // Get node references
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
        _cameraPivot = GetNode<Node3D>("Node3D");
        _playerCamera = _cameraPivot.GetNode<Camera3D>("Camera3D");
        
        // noclip vars
        _originalCollisionLayer = CollisionLayer;
        _originalCollisionMask = CollisionMask;
        _originalGravityMultiplier = _gravityMultiplier;
            
        if (Engine.IsEditorHint()) return;
        
        // Initialize state
        ValidateInputActions();
        CaptureMouse();
        CacheOriginalDimensions();
    }

    private void CacheOriginalDimensions()
    {
        if (_collisionShape.Shape is CapsuleShape3D capsule)
        {
            _originalHeight = capsule.Height;
            _currentHeight = _originalHeight;
        }
        
        _originalPivotPosition = _cameraPivot.Position;
        _currentPivotPosition = _originalPivotPosition;
        
        // Cache mesh position if exists
        var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (mesh != null)
        {
            _meshOriginalPosition = mesh.Position;
        }
    }

    private void ValidateInputActions()
    {
        foreach (var action in RequiredActions)
        {
            _actionStates[action] = InputMap.HasAction(action);
            if (!_actionStates[action])
            {
                GD.PushWarning($"Input action '{action}' is missing! Related functionality disabled.");
            }
        }
        
        // Validate optional actions
        _actionStates[_sprintAction] = InputMap.HasAction(_sprintAction);
        _actionStates[_crouchAction] = InputMap.HasAction(_crouchAction);
        _actionStates[_noclipAction] = InputMap.HasAction(_noclipAction);
    }

    private bool IsActionValid(StringName action)
    {
        return _actionStates.TryGetValue(action, out var exists) && exists;
    }
    #endregion

    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;
        
        if (@event.IsActionPressed("ui_cancel")) //you should remove this if you make a full game at some point
        {
            ToggleMouseCapture();
        }

        if (IsActionValid("_enableNoclip"))
        {
            if (_enableNoclip && @event.IsActionPressed("move_noclip"))
            {
                ToggleNoclip();
            }
        }


        if (!_enableLook || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        
        if (@event is InputEventMouseMotion mouseMotion)
        {
            HandleMouseLook(mouseMotion.Relative);
        }
    }



    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint()) return;
        
        var fDelta = (float)delta;
        
        ApplyGravity(fDelta);
        HandleJump(fDelta);
        HandleMovement(fDelta);
        
        if (_enableCrouch)
        {
            HandleCrouch(fDelta);
        }
        
        HandleSprint();
        
        MoveAndSlide();
        UpdateTimers(fDelta);
        AdjustMeshPosition();
    }

    #region Look Logic
    private void HandleMouseLook(Vector2 relative)
    {
        // Horizontal rotation (player body)
        RotateY(Mathf.DegToRad(-relative.X * _mouseSensitivity));
        
        // Vertical rotation (camera pivot)
        float verticalFactor = _invertYAxis ? 1 : -1;
        _verticalLookRotation += relative.Y * _mouseSensitivity * verticalFactor;
        _verticalLookRotation = Mathf.Clamp(
            _verticalLookRotation, 
            -_verticalLookLimit, 
            _verticalLookLimit
        );
        
        _cameraPivot.RotationDegrees = new Vector3(
            _verticalLookRotation,
            0,
            0
        );
    }
    #endregion

    #region Movement Logic
    private void ApplyGravity(float delta)
    {
        if (!IsOnFloor())
        {
            var gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
            Velocity = new Vector3(
                Velocity.X,
                Velocity.Y - (gravity * _gravityMultiplier * delta),
                Velocity.Z
            );
        }
        else if (Velocity.Y < 0)
        {
            Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
        }
    }

    private void HandleJump(float delta)
    {
        if (!_enableJump) return;
        
        if (IsOnFloor())
        {
            _coyoteTimer = _coyoteTime;
            _isJumping = false;
        }
        else
        {
            _coyoteTimer -= delta;
        }

        if (IsActionValid(_jumpAction) && Input.IsActionPressed(_jumpAction))
        {
            _jumpBufferTimer = _jumpBufferTime;
        }

        if (_jumpBufferTimer > 0 && _coyoteTimer > 0 && !_isJumping)
        {
            Velocity = new Vector3(Velocity.X, _jumpVelocity, Velocity.Z);
            _isJumping = true;
            _jumpBufferTimer = 0;
            _coyoteTimer = 0;
        }
    }

    private void HandleMovement(float delta)
    {
        if (!_enableMovement) return;

        if (_isNoclip)
        {
            HandleNoclipMovement(delta);
            return;
        }
        
        // Get input axis
        var inputAxis = new Vector2(
            GetAxisInput(_moveRightAction, _moveLeftAction),
            GetAxisInput(_moveForwardAction, _moveBackwardAction)
        );
        
        //- inputAxis.Y because the camera "looking" is inverted
        var direction = new Vector3(inputAxis.X, 0,- inputAxis.Y).Normalized();
        
        // Convert direction to global space
        direction = GlobalTransform.Basis * direction;
        
        // Apply slope limitation
        if (IsOnFloor() && direction != Vector3.Zero)
        {
            direction = LimitSlopeDirection(direction);
        }
        
        var targetSpeed = _isSprinting ? _baseSpeed * _sprintMultiplier : _baseSpeed;
        var controlFactor = IsOnFloor() ? 1.0f : _airControl;
        
        var horizontalVelocity = new Vector3(Velocity.X, 0, Velocity.Z);
        var targetVelocity = direction * targetSpeed;
        
        // Accelerate/decelerate
        horizontalVelocity = horizontalVelocity.Lerp(
            targetVelocity,
            (direction != Vector3.Zero ? _acceleration : _deceleration) * controlFactor * delta
        );
        
        Velocity = new Vector3(horizontalVelocity.X, Velocity.Y, horizontalVelocity.Z);
    }



    private float GetAxisInput(StringName positiveAction, StringName negativeAction)
    {
        float value = 0;
        if (IsActionValid(positiveAction) && Input.IsActionPressed(positiveAction)) value += 1;
        if (IsActionValid(negativeAction) && Input.IsActionPressed(negativeAction)) value -= 1;
        return value;
    }

    private Vector3 LimitSlopeDirection(Vector3 direction)
    {
        return GetFloorNormal().AngleTo(Vector3.Up) > Mathf.DegToRad(_slopeMaxAngle) ? direction.Slide(GetFloorNormal()).Normalized() : direction;
    }
    #endregion

    #region Advanced Features
    private void HandleNoclipMovement(float delta)
    {
        float vertical = 0;
        if (IsActionValid(_jumpAction) && Input.IsActionPressed(_jumpAction)) vertical += 1;
        if (IsActionValid(_crouchAction) && Input.IsActionPressed(_crouchAction)) vertical -= 1;
    
        float forward = IsActionValid(_moveForwardAction) && Input.IsActionPressed(_moveForwardAction) ? 1 : 0;
        float backward = IsActionValid(_moveBackwardAction) && Input.IsActionPressed(_moveBackwardAction) ? 1 : 0;
        float right = IsActionValid(_moveRightAction) && Input.IsActionPressed(_moveRightAction) ? 1 : 0;
        float left = IsActionValid(_moveLeftAction) && Input.IsActionPressed(_moveLeftAction) ? 1 : 0;
        
        Vector3 direction = Vector3.Zero;
        direction += -_playerCamera.GlobalTransform.Basis.Z * (forward - backward);
        direction += _playerCamera.GlobalTransform.Basis.X * (right - left);
        direction += Vector3.Up * vertical;
        direction = direction.Normalized();
        
        Vector3 targetVelocity = direction * _baseSpeed;
        Velocity = Velocity.Lerp(
            targetVelocity,
            _acceleration * delta
        );
    }
    
    private void HandleSprint()
    {
        _isSprinting = _enableSprint && 
                       IsActionValid(_sprintAction) && 
                       Input.IsActionPressed(_sprintAction) &&
                       IsOnFloor() &&
                       !_isCrouching &&
                       // Only sprint when moving forward
                       Input.IsActionPressed(_moveForwardAction) && 
                       !Input.IsActionPressed(_moveBackwardAction);
    }

    private void HandleCrouch(float delta)
    {
        if (!_enableCrouch || !IsActionValid(_crouchAction)) return;
        
        var wantsCrouch = Input.IsActionPressed(_crouchAction);

        // Crouch transition logic
        if (wantsCrouch)
        {
            if (!_isCrouching && IsOnFloor())
            {
                // Start crouching
                _isCrouching = true;
                _currentHeight = _originalHeight * 0.6f;
                _currentPivotPosition = _originalPivotPosition * 0.6f;
            }
        }
        else if (_isCrouching)
        {
            // Check if we can stand up
            var spaceState = GetWorld3D().DirectSpaceState;
            var query = new PhysicsShapeQueryParameters3D
            {
                Shape = _collisionShape.Shape,
                Transform = GlobalTransform.Translated(Vector3.Up * (_originalHeight - _currentHeight)),
                CollisionMask = CollisionMask,
                Exclude = new Godot.Collections.Array<Rid> { GetRid() }
            };
            
            var canStandUp = spaceState.CollideShape(query).Count == 0;
            
            if (canStandUp)
            {
                _isCrouching = false;
                _currentHeight = _originalHeight;
                _currentPivotPosition = _originalPivotPosition;
            }
        }
        
        // Smooth height transition
        if (_collisionShape.Shape is CapsuleShape3D capsule)
        {
            capsule.Height = Mathf.Lerp(capsule.Height, _currentHeight, 15 * delta);
        }
        
        // Smooth camera pivot transition
        _cameraPivot.Position = _cameraPivot.Position.Lerp(
            _currentPivotPosition, 
            15 * delta
        );
        
        // Prevent sprinting while crouched
        if (_isCrouching) _isSprinting = false;
    }

    private void AdjustMeshPosition()
    {
        var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
        if (mesh == null) return;
        
        // Keep mesh aligned with collision shape
        if (_collisionShape.Shape is CapsuleShape3D capsule)
        {
            var heightDifference = _originalHeight - capsule.Height;
            mesh.Position = new Vector3(
                _meshOriginalPosition.X,
                _meshOriginalPosition.Y - heightDifference / 2,
                _meshOriginalPosition.Z
            );
        }
    }
    #endregion

    #region Utility Methods
    private void CaptureMouse()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void ToggleMouseCapture()
    {
        Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
            ? Input.MouseModeEnum.Visible
            : Input.MouseModeEnum.Captured;
    }
    
    private void ToggleNoclip()
    {
        if(!_enableNoclip) return;
        _isNoclip = !_isNoclip;
        
        //todo implement noclip logic (collisions + movement)

        _gravityMultiplier = _isNoclip ? 0 : _originalGravityMultiplier;
        CollisionLayer = _isNoclip ? 0 : _originalCollisionLayer;
        CollisionMask = _isNoclip ? 0 : _originalCollisionMask;
        
        //Velocity reset 
        Velocity = Vector3.Zero;
        
    }

    private void UpdateTimers(float delta)
    {
        if (_coyoteTimer > 0) _coyoteTimer -= delta;
        if (_jumpBufferTimer > 0) _jumpBufferTimer -= delta;
    }
    #endregion
}