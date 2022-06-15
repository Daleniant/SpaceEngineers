string groupNameTag = "HudGroup";

List<weaponSystem> weaponSystemList = new List<weaponSystem>();

Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update1;

    if (!GrabGroups())
        Runtime.UpdateFrequency = UpdateFrequency.None;
}

void Main(string argument, UpdateType updateSource)
{
    foreach (weaponSystem ws in weaponSystemList)
    {
        if (argument == "Lock")
            ws.isSearching = !ws.isSearching;
        ws.DisplayHud();
    }
}

bool GrabGroups()
{
    List<IMyBlockGroup> groupList = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(groupList);

    foreach (IMyBlockGroup group in groupList)
    {
        if (group.Name.ToLower().Contains(groupNameTag.ToLower()))
        {
            weaponSystemList.Add(new weaponSystem(group));
        }
    }

    if (weaponSystemList.Count == 0)
    {
        Echo($"ERROR: No '{groupNameTag} groups were found!");
        return false;
    }
    return true;
}

class weaponSystem
{
    public bool isSearching = false;
    bool isLocked = false;
    MyDetectedEntityInfo target;
    double timeSinceLock;
    MyTuple<Vector3D, int> targetOffset = new MyTuple<Vector3D, int>(Vector3D.Zero, 0);

    int currentFrames, tapped, currentType, previousFrames;
    List<MyTuple<IMyUserControllableGun, int>> weaponInfo = new List<MyTuple<IMyUserControllableGun, int>>();
    List<MyTuple<int, int, int, string>> weaponType = new List<MyTuple<int, int, int, string>>();

    MyTuple<IMyShipController, IMyTextPanel> controllerSystem = new MyTuple<IMyShipController, IMyTextPanel>(null, null);

    List<MyTuple<IMyCameraBlock, IMyTextPanel, int>> cameraSystem = new List<MyTuple<IMyCameraBlock, IMyTextPanel, int>>();
    List<IMyCameraBlock> refCameras = new List<IMyCameraBlock>();

    double blockSize;
    Color mainColor = new Color(r: 50, g: 210, b: 50), textColor = new Color(r: 30, g: 130, b: 30);

    public weaponSystem(IMyBlockGroup group)
    {
        List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
        group.GetBlocks(blocks);

        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));
        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));
        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));
        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));
        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));
        weaponType.Add(new MyTuple<int, int, int, string>(0, 0, 0, ""));

        foreach (IMyTerminalBlock b in blocks)
        {
            if (b is IMyUserControllableGun)
                WeaponsInit((IMyUserControllableGun)b);

            else if (b is IMyShipController)
                controllerSystem.Item1 = (IMyShipController)b;

            else if (b is IMyTextPanel)
                cameraLcdInit(null, (IMyTextPanel)b);

            else if (b is IMyCameraBlock)
                cameraLcdInit((IMyCameraBlock)b, null);
        }
        blockSize = controllerSystem.Item1.CubeGrid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 0.5;
        currentFrames = 0; previousFrames = 0; tapped = 0; currentType = 0;
    }

    public void DisplayHud()
    {
        if (!controllerSystem.Item1.IsUnderControl)
            return;

        if (isSearching)
            TargetLock();
        SetCurrentWeaponType();

        MatrixD viewMat = new MatrixD();
        IMyTextPanel lcd = null;
        foreach (MyTuple<IMyCameraBlock, IMyTextPanel, int> cs in cameraSystem)
        {
            if (cs.Item1.IsActive)
            {
                viewMat = cs.Item1.WorldMatrix;
                lcd = cs.Item2;
                break;
            }
        }

        if (lcd == null)
        {
            lcd = controllerSystem.Item2;
            if (lcd == null)
                return;
            viewMat = controllerSystem.Item1.WorldMatrix;
        }

        lcd.Script = "";
        lcd.ScriptBackgroundColor = Color.Transparent;
        lcd.ContentType = ContentType.SCRIPT;

        MySpriteDrawFrame frame = lcd.DrawFrame();

        MySprite infoOutput = new MySprite(SpriteType.TEXT, "Current Weapon Type: " + weaponType[currentType].Item4, color: textColor, fontId: "White", alignment: TextAlignment.LEFT);
        infoOutput.Position = new Vector2(80, 330) + (lcd.TextureSize - lcd.SurfaceSize) / 2;
        infoOutput.RotationOrScale = 0.5f;
        frame.Add(infoOutput);


        infoOutput.Data = isSearching ? "Lock Status: SEARCHING..." : "Lock Status: NOT LOCKED";
        infoOutput.Color = new Color(r: 160, g: 110, b: 30);
        if (isLocked) {
            infoOutput.Data = $"Lock Status: LOCKED";
            infoOutput.Color = textColor;
            TargetTracking();
            foreach (MyTuple<IMyUserControllableGun, int> weapon in weaponInfo)
            {
                if(weapon.Item2 == currentType)
                {
                    PathPredicion(weapon, frame, viewMat, lcd);
                    break;
                }
            }
        }

        infoOutput.Position -= new Vector2(0, 180);
        frame.Add(infoOutput);

        double dist = weaponType[currentType].Item1;
        foreach (MyTuple<IMyUserControllableGun, int> weapon in weaponInfo)
        {
            if (weapon.Item2 == currentType)
            {
                if (isLocked)
                    dist = Vector3D.Distance(weapon.Item1.GetPosition(), target.Position);
                if (dist > weaponType[currentType].Item1 + 100)
                    continue;
                PIP(weapon, dist, frame, viewMat, lcd);
            }
        }

        frame.Dispose();
    }

    public void TargetLock()
    {
        if(!target.IsEmpty()){
            target = new MyDetectedEntityInfo();
            isLocked = false;
            isSearching = false;
            timeSinceLock = 0;
            return;
        }
        foreach (IMyCameraBlock camera in refCameras)
        {
            if (!camera.CanScan(5000))
                continue;
            target = camera.Raycast(5000);

            if (!target.IsEmpty() && (target.Type == MyDetectedEntityType.LargeGrid || target.Type == MyDetectedEntityType.SmallGrid)){
                isLocked = true; 
                isSearching = false;
                timeSinceLock = 0;
                return;
            }
        }
    }

    void TargetTracking()
    {
        timeSinceLock += 1 / 60;
        if(timeSinceLock > 5){
            target = new MyDetectedEntityInfo();
            timeSinceLock = 0; isLocked = false;
            return;
        }

        Vector3D linearApprox = target.Position + targetOffset.Item1 + target.Velocity * (float)timeSinceLock;
        double scanDist = Vector3D.Distance(controllerSystem.Item1.GetPosition(), linearApprox);
        int usedCameras = 0;
        MyDetectedEntityInfo tempTarget = new MyDetectedEntityInfo();

        foreach (IMyCameraBlock camera in refCameras)
        {
            if (camera.CanScan(scanDist))
            {
                usedCameras++;
                if (timeSinceLock > 3) 
                    tempTarget = camera.Raycast(camera.AvailableScanRange);
                else 
                    tempTarget = camera.Raycast(linearApprox);

                if (!tempTarget.IsEmpty() && tempTarget.EntityId == target.EntityId)
                {
                    target = tempTarget;
                    timeSinceLock = 0;
                    targetOffset.Item1 = Vector3D.Zero;
                    targetOffset.Item2 = 0;
                    return;
                }
            }
        }
        if (usedCameras == 0)
            return;

        int coef = targetOffset.Item2 / 6;
        switch (targetOffset.Item2 % 6)
        {
            case 0: targetOffset.Item1 = target.Orientation.Forward * 7 * coef; break;
            case 1: targetOffset.Item1 = target.Orientation.Forward * (-7) * coef; break;
            case 2: targetOffset.Item1 = target.Orientation.Up * 7 * coef; break;
            case 3: targetOffset.Item1 = target.Orientation.Up * (-7) * coef; break;
            case 4: targetOffset.Item1 = target.Orientation.Right * 7 * coef; break;
            case 5: targetOffset.Item1 = target.Orientation.Right * (-7) * coef; break;
        }
        targetOffset.Item2++;
    }

    void WeaponsInit(IMyUserControllableGun weapon)
    {
        string name = weapon.CustomName.ToLower();

        if (name.Contains("autocannon") || name.Contains("gatling")){
            weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 0));
            if (weaponType[0].Item1 == 0)
                weaponType[0] = new MyTuple<int, int, int, string>(800, 400, 0, "Autocannon/Gatling");
            return;
        }

        else if (name.Contains("rocket") || name.Contains("missile")){
            weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 1));
            if (weaponType[1].Item1 == 0)
                weaponType[1] = new MyTuple<int, int, int, string>(800, 100, 600, "Rocket/Missile");
            return;
        }

        else if (name.Contains("assault")){
            weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 2));
            if (weaponType[2].Item1 == 0)
                weaponType[2] = new MyTuple<int, int, int, string>(1400, 500, 0, "Assault Cannon");
            return;
        }

        else if(name.Contains("railgun"))
        {
            if (weapon.CubeGrid.GridSizeEnum == MyCubeSize.Small){
                weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 3));
                if (weaponType[3].Item1 == 0)
                    weaponType[3] = new MyTuple<int, int, int, string>(1400, 1000, 0, "S. Railgun");
                return;
            }
            else{
                weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 4));
                if (weaponType[4].Item1 == 0)
                    weaponType[4] = new MyTuple<int, int, int, string>(2000, 2000, 0, "L. Railgun");
                return;
            }
        }

        else if (name.Contains("artillery"))
        {
            weaponInfo.Add(new MyTuple<IMyUserControllableGun, int>(weapon, 5));
            if (weaponType[5].Item1 == 0)
                weaponType[5] = new MyTuple<int, int, int, string>(2000, 500, 0, "Artillery");
            return;
        }
    }

    void cameraLcdInit(IMyCameraBlock camera, IMyTextPanel lcd)
    {
        string name = lcd == null ? camera.CustomName.ToLower() : lcd.CustomName.ToLower();
        if(name.Contains("con") && controllerSystem.Item2 == null){
            controllerSystem.Item2 = lcd;
            return;
        }
        else if(name.Contains("ref") && camera != null){
            camera.EnableRaycast = true;
            refCameras.Add(camera);
            return;
        }

        int setNumber = -1;
        if (name[name.Length - 2] >= '0' && name[name.Length - 2] <= '9'){
            try { 
                setNumber = 10 * (name[name.Length - 2] - '0') + (name[name.Length - 1] - '0'); 
            }
            catch { return; }
        }
        else{
            try { 
                setNumber = name[name.Length - 1] - '0'; 
            }
            catch { return; }
        }

        for (int i = 0; i < cameraSystem.Count; i++)
        {
            if (cameraSystem[i].Item3 == setNumber)
            {
                if (lcd == null)
                    cameraSystem[i] = new MyTuple<IMyCameraBlock, IMyTextPanel, int>(camera, cameraSystem[i].Item2, setNumber);
                else
                    cameraSystem[i] = new MyTuple<IMyCameraBlock, IMyTextPanel, int>(cameraSystem[i].Item1, lcd, setNumber);
                return;
            }
        }
        cameraSystem.Add(new MyTuple<IMyCameraBlock, IMyTextPanel, int>(camera, lcd, setNumber));
    }

    void SetCurrentWeaponType()
    {
        currentFrames++;

        foreach (MyTuple<IMyUserControllableGun, int> weapon in weaponInfo)
        {
            if (weapon.Item1.IsShooting && !weapon.Item1.Shoot){
                currentType = weapon.Item2;
                return;
            }
        }

        if (controllerSystem.Item1.RollIndicator > 0)
        {
            if (tapped == 0){
                tapped = 1;
                previousFrames = currentFrames;
                return;
            }
            else if (tapped == 1)
            {
                tapped = 0;
                if ((currentFrames - previousFrames) < 60 && (currentFrames - previousFrames) > 1)
                {
                    currentType = (currentType + 1) % 6;
                    while (weaponType[currentType].Item1 == 0)
                        currentType = (currentType + 1) % 6;
                }
                currentFrames = 0;
                previousFrames = 0;
            }
        }
    }

    void PIP(MyTuple<IMyUserControllableGun, int> weapon, double dist, MySpriteDrawFrame frame, MatrixD viewMat, IMyTextPanel lcd)
    {
        Vector3D dirVec = Vector3D.Normalize(weapon.Item1.WorldMatrix.Forward);

        Vector3D velocity = dirVec * weaponType[currentType].Item2 + controllerSystem.Item1.GetShipVelocities().LinearVelocity;
        Vector3D acceleration;
        if (weaponType[currentType].Item3 == 0)
            acceleration = controllerSystem.Item1.GetNaturalGravity();
        else
            acceleration = dirVec * weaponType[currentType].Item3;                

        Vector3D pip = weapon.Item1.GetPosition() + CalculateBallistic(dist, velocity, acceleration / 2);
        Vector2 lcdProjection = ProjectOnHUD(pip, viewMat, lcd);

        if (lcdProjection != Vector2.Zero){
            MySprite predictedPoint = new MySprite(SpriteType.TEXTURE, "CircleHollow", position: lcdProjection, size: Vector2.One * 12, color: mainColor);

            frame.Add(predictedPoint);
        }
    }

    void PathPredicion(MyTuple<IMyUserControllableGun, int> weapon, MySpriteDrawFrame frame, MatrixD viewMat, IMyTextPanel lcd)
    {
        Vector3D dirVec = Vector3D.Normalize(target.Position - weapon.Item1.GetPosition());

        Vector3D velocity = dirVec * weaponType[currentType].Item2 + controllerSystem.Item1.GetShipVelocities().LinearVelocity;
        Vector3D acceleration = controllerSystem.Item1.GetNaturalGravity() + dirVec * weaponType[currentType].Item3;

        Vector3D pathPrediction = target.Position;
        pathPrediction += target.Velocity * CalculateTime(Vector3D.Distance(controllerSystem.Item1.GetPosition(), target.Position), velocity, acceleration / 2);

        Vector2 lcdProjection = ProjectOnHUD(pathPrediction, viewMat, lcd);

        if (lcdProjection != Vector2.Zero){
            MySprite predictedPoint = new MySprite(SpriteType.TEXTURE, "SquareHollow", position: lcdProjection, size: Vector2.One * 14, color: new Color(r: 255, g: 50, b: 50));
            frame.Add(predictedPoint);

            predictedPoint.Size = Vector2.One * 12;
            frame.Add(predictedPoint);
        }
    }

    Vector3D CalculateBallistic(double targetDist, Vector3D velocity, Vector3D acceleration)
    {
        double leftT = 0, rightT = 4;
        while (leftT < rightT)
        {
            double midT = (leftT + rightT) / 2;
            Vector3D projectile = velocity * midT + acceleration * midT * midT;

            if (projectile.Length() + 0.5 < targetDist)
                leftT = midT;
            else if (projectile.Length() - 0.5 > targetDist)
                rightT = midT;
            else
                return projectile;
        }

        return Vector3D.Zero;
    }

    float CalculateTime(double distance, Vector3D velocity, Vector3D acceleration)
    {
        double leftT = 0, rightT = 4;
        Vector3D projectile;
        while (leftT < rightT)
        {
            double midT = (leftT + rightT) / 2;
            projectile = velocity * midT + acceleration * midT * midT;

            if (projectile.Length() + 0.5 < distance)
                leftT = midT;
            else if (projectile.Length() - 0.5 > distance)
                rightT = midT;
            else
                return (float)midT;
        }

        return 0;
    }

    Vector2 ProjectOnHUD(Vector3D testPoint, MatrixD viewMat, IMyTextPanel lcd)
    {

        MatrixD lcdMat = lcd.WorldMatrix;
        Vector3D viewP = viewMat.Translation + viewMat.Forward * (blockSize / 2);

        RayD rayToCam = new RayD(testPoint, Vector3D.Normalize(viewP - testPoint));
        PlaneD lcdPlane = new PlaneD(lcdMat.Translation + lcdMat.Forward * (blockSize / 2), lcdMat.Forward);

        double? distToLcd = rayToCam.Intersects(lcdPlane);

        if (distToLcd.HasValue)
        {
            Vector3D projection = rayToCam.Position + Vector3D.Normalize(rayToCam.Direction) * distToLcd.Value;
            projection = Vector3D.TransformNormal(projection - lcdMat.Translation, MatrixD.Transpose(lcdMat));
            projection *= lcd.SurfaceSize.X / blockSize; //change from meters to pixels

            return new Vector2(lcd.TextureSize.X / 2 + (float)projection.X, lcd.TextureSize.Y / 2 - (float)projection.Y);
        }
        return Vector2.Zero;
    }
}
