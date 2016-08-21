/****************************************************/
/****************************************************/
/*                                                  */
/*                                                  */
/*                 Metiri ANPR v0.2                 */
/*          Automatic License Plate Reader          */
/*                       &                          */
/*                  Speed Radar                     */
/*                                                  */
/*                                                  */
/****************************************************/
/****************************************************/

/*
 * Changelog
 * v0.2
 * - Only allowed to use in a vehicle
 * - INI settings file
 *    - Custom key bindings
 *    - Customs settings
 * - "Snap To Ground" feature (still needs work)
 * - Better UI
 * - Simple Violations
 *   - Simple "session" vehicle database (remembers license plates and their violations for a play session)
 * - Flag/unflag vehicles
 *   - Add/remove marker above flagged vehicles
 * 
 * Upcoming features
 * - Violations
 *   - Custom violations
 *   - Violations play sound
 *   - Mark violations 360 degrees around player (not just the location marked)
 *   - Violations marking above car
 * - XML Database for violations
 *   - Save license plate, names and vehicle info
 *   - Load during gameplay with a chance of spawning that vehicle and person
 */

using GTA;
using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

public class ANPR : Script
{
    /* INI file */
    private ScriptSettings config;

    /* Key variables */
    private Keys activeKey, moveForward, moveBackward, moveLeft, moveRight, moveDown, moveUp, scanKey, flagVehicle;

    /* Mod variables */
    private bool active, snapToGround, speedUnits;
    private Ped player;
    private GTA.Math.Vector3 markerLoc;
    private float forwardOffset, rightOffset, heightOffset, maxDistance, highSpeed, percentage;
    private Vehicle targetVeh, flagged;

    /* Database */
    private Dictionary<String, String> VehicleDatabase;

    /* Random number */
    private Random rand = new Random( new Random().Next( 1, 99999 ) ); // Super randomness

    public ANPR()
    {
        /* INI file */
        config = ScriptSettings.Load( "scripts\\ANPR.ini" );

        /* Custom key binding */
        activeKey = config.GetValue<Keys>( "KEYS", "ActivateKey", Keys.NumPad5 );
        moveForward = config.GetValue<Keys>( "KEYS", "ForwardKey", Keys.NumPad8 );
        moveBackward = config.GetValue<Keys>( "KEYS", "BackwardKey", Keys.NumPad2 );
        moveLeft = config.GetValue<Keys>( "KEYS", "LeftKey", Keys.NumPad4 );
        moveRight = config.GetValue<Keys>( "KEYS", "RightKey", Keys.NumPad6 );
        moveDown = config.GetValue<Keys>( "KEYS", "DownKey", Keys.NumPad7 );
        moveUp = config.GetValue<Keys>( "KEYS", "UpKey", Keys.NumPad9 );
        scanKey = config.GetValue<Keys>( "KEYS", "ScanKey", Keys.NumPad3 );
        flagVehicle = config.GetValue<Keys>( "KEYS", "FlagVehicle", Keys.NumPad1 );

        /* Set variables */
        active = false;
        snapToGround = config.GetValue<bool>( "SETTINGS", "SnapToGround", true ); ;
        forwardOffset = 5f;
        rightOffset = 0f;
        heightOffset = 0f;
        maxDistance = config.GetValue<float>( "SETTINGS", "MaxDistance", 20f );
        highSpeed = config.GetValue<float>( "SETTINGS", "HighSpeed", 50f );
        percentage = config.GetValue<float>( "SETTINGS", "PercentageOfHit", 10f );
        flagged = null;

        /* Get and parse units */
        string units = config.GetValue<String>( "SETTINGS", "Units", "mph" );
        units.ToLower();
        if( units == "kph" )
            speedUnits = false;
        else
            speedUnits = true;

        VehicleDatabase = new Dictionary<string, string>();

        /* Bind events */
        Tick += OnTick;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;

        Interval = config.GetValue<int>( "ADVANCED", "TickInterval", 10 ); ;
    }

    void OnTick( object sender, EventArgs e )
    {
        if( active && Game.Player.Character.IsInVehicle())
        {
            /* UI */
            UIElement vehicleUI = new UIText( "VEHICLE: ", new Point( 240, 560 ), 0.4f, Color.White );
            UIElement colorUI = new UIText( "COLOR: ", new Point( 240, 575 ), 0.4f, Color.White );
            UIElement plateUI = new UIText( "PLATE: ", new Point( 240, 590 ), 0.4f, Color.White );
            UIElement speedUI = new UIText( "SPEED: ", new Point( 240, 605 ), 0.4f, Color.White );
            UIElement violationUI = new UIText( "", new Point( 240, 620 ), 0.4f, Color.Red );

            /* Player model */
            player = Game.Player.Character;

            /* Draw maker */
            if( snapToGround )
            {
                GTA.Math.Vector3 temp = player.GetOffsetInWorldCoords( new GTA.Math.Vector3( rightOffset, forwardOffset, heightOffset ) );
                float height = World.GetGroundHeight( temp );
                markerLoc = new GTA.Math.Vector3( temp.X, temp.Y, height );
            }
            else
            {
                markerLoc = player.GetOffsetInWorldCoords( new GTA.Math.Vector3( rightOffset, forwardOffset, heightOffset ) );
            }

            GTA.World.DrawMarker( MarkerType.VerticalCylinder, markerLoc, GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, new GTA.Math.Vector3( 1, 1, 1 ), Color.Red );

            /* Get closest vehicles in max radius */
            Vehicle[] closestVeh = World.GetNearbyVehicles( player, maxDistance );

            /* Check to see if a vehicle is near the marker */
            for( int i = 0; i < closestVeh.Length; i++ )
            {
                /* If not the player's vehicle */
                if( closestVeh[ i ] != player.CurrentVehicle && closestVeh[ i ].Exists() )
                {
                    /* Check distance between car and marker position */
                    if( World.GetDistance( markerLoc, closestVeh[ i ].Position ) < 3f )
                    {
                        targetVeh = closestVeh[ i ];

                        /* Setup UI */
                        vehicleUI = new UIText( "VEHICLE: " + targetVeh.DisplayName.ToString(), new Point( 240, 560 ), 0.4f, Color.White );
                        colorUI = new UIText( "COLOR: " + targetVeh.PrimaryColor.ToString(), new Point( 240, 575 ), 0.4f, Color.White );
                        plateUI = new UIText( "PLATE: " + targetVeh.NumberPlate.ToString(), new Point( 240, 590 ), 0.4f, Color.White );

                        /* Color based on speed */
                        /* Convert to units */
                        float speed;

                        if( speedUnits )
                            speed = targetVeh.Speed;
                        else
                            speed = targetVeh.Speed * 1.60934f;

                        if( speed >= highSpeed / 2 )
                            speedUI = new UIText( "SPEED: " + speed.ToString(), new Point( 240, 605 ), 0.4f, Color.Yellow );
                        else if( targetVeh.Speed >= highSpeed )
                            speedUI = new UIText( "SPEED: " + speed.ToString(), new Point( 240, 605 ), 0.4f, Color.Red );
                        else
                            speedUI = new UIText( "SPEED: " + speed.ToString(), new Point( 240, 605 ), 0.4f, Color.White );

                        /* Violations system */
                        /* Generate a violation */
                        if( !VehicleDatabase.ContainsKey( targetVeh.NumberPlate.ToString() ) )
                        {
                            string violation = "None";

                            if( rand.Next( 1, 100 ) < percentage )
                                violation = ParseViolation( new Random().Next( 0, 4 ) ); // Random violation

                            VehicleDatabase.Add( targetVeh.NumberPlate.ToString(), violation ); // Add vehicle and violation to database
                        }

                        /* Check database for vehicle violation */
                        if( VehicleDatabase.ContainsKey( targetVeh.NumberPlate.ToString() ) && VehicleDatabase[ targetVeh.NumberPlate.ToString() ] != "None" )
                        {
                            /* Play sound */
                            //GTA.Native.Function.Call( GTA.Native.Hash.PLAY_SOUND_FRONTEND, -1, "PICK_UP", "HUD_FRONTEND_DEFAULT_SOUNDSET" );

                            /* Display violation message */
                            violationUI = new UIText( "VIOLATION: " + VehicleDatabase[ targetVeh.NumberPlate.ToString() ].ToString(), new Point( 240, 620 ), 0.4f, Color.Red );
                        }

                        break;
                    }
                }
            }

            /* Draw marker above flagged car */
            if( flagged != null )
            {
                GTA.World.DrawMarker( MarkerType.UpsideDownCone, flagged.Position + new GTA.Math.Vector3( 0, 0, 2 ), GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, new GTA.Math.Vector3( 1, 1, 1 ), Color.Red );
            }

            /* Draw UI */
            vehicleUI.Draw();
            colorUI.Draw();
            plateUI.Draw();
            speedUI.Draw();
            violationUI.Draw();
        }
    }

    void OnKeyDown( object sender, KeyEventArgs e )
    {
        /* Controls */
        if( active )
        {
            if( e.KeyCode == moveForward && forwardOffset < maxDistance )
                forwardOffset += 0.5f;
            if( e.KeyCode == moveBackward && forwardOffset > -maxDistance )
                forwardOffset -= 0.5f;
            if( e.KeyCode == moveLeft && rightOffset < maxDistance )
                rightOffset -= 0.5f;
            if( e.KeyCode == moveRight && rightOffset > -maxDistance )
                rightOffset += 0.5f;
            if( e.KeyCode == moveDown )
                heightOffset -= 0.5f;
            if( e.KeyCode == moveUp )
                heightOffset += 0.5f;
        }
    }

    void OnKeyUp( object sender, KeyEventArgs e )
    {
        /* Enable mod */
        if( e.KeyCode == activeKey )
        {
            if( active )
                UI.ShowSubtitle( "Metiri ANPR & Speed Radar deactivated" );
            else
                UI.ShowSubtitle( "Metiri ANPR & Speed Radar activated" );

            active = !active;

            forwardOffset = 5f;
            rightOffset = 0f;
            heightOffset = 0f;
        }

        /* Scan for flagged vehicles*/
        if( active && e.KeyCode == scanKey && Game.Player.Character.IsInVehicle())
        {
            UI.ShowSubtitle( "Scanned for flagged vehicles in your area" );
            flagged = null;

            if( flagged == null )
            {
                Vehicle[] flaggedVeh = World.GetNearbyVehicles( player, 1000 );

                for( int i = 0; i < flaggedVeh.Length; i++ )
                {
                    if( flaggedVeh[ i ] != player.CurrentVehicle &&
                        VehicleDatabase.ContainsKey( flaggedVeh[ i ].NumberPlate.ToString() ) &&
                        VehicleDatabase[ flaggedVeh[ i ].NumberPlate.ToString() ] == "FLAGGED BY OFFICERS" )
                    {
                        flagged = flaggedVeh[ i ];
                        break;
                    }
                }
            }
        }

        /* Flag vehicle */
        if( active && e.KeyCode == flagVehicle && Game.Player.Character.IsInVehicle() )
        {
            /* Get closest vehicles in max radius */
            Vehicle[] closestVeh = World.GetNearbyVehicles( player, maxDistance );

            /* Check to see if a vehicle is near the marker */
            for( int i = 0; i < closestVeh.Length; i++ )
            {
                /* If not the player's vehicle */
                if( closestVeh[ i ] != player.CurrentVehicle && closestVeh[ i ].Exists() )
                {
                    /* Check distance between car and marker position */
                    if( World.GetDistance( markerLoc, closestVeh[ i ].Position ) < 3f )
                    {
                        targetVeh = closestVeh[ i ];
                        break;
                    }
                }
            }

            /* Change violation to flagged */
            if( VehicleDatabase[ targetVeh.NumberPlate.ToString() ] != "FLAGGED BY OFFICERS" )
            {
                VehicleDatabase[ targetVeh.NumberPlate.ToString() ] = "FLAGGED BY OFFICERS"; // Change vehicle's violation in the database
            }
            else
            {
                VehicleDatabase[ targetVeh.NumberPlate.ToString() ] = "None"; // Change vehicle's violation in the database
                flagged = null;
            }
        }
    }

    string ParseViolation( int x )
    {
        switch( x )
        {
            case 0:
                return "WARRANT FOR ARREST";
            case 1:
                return "NO INSURANCE";
            case 2:
                return "NOT REGISTERED";
            case 3:
                return "INVOLVED IN HIT & RUN";
            case 4:
                return "OUTSTANDING TICKETS";
            default:
                return "ERROR";
        }
    }
}