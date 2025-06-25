#!/usr/bin/env python3
"""
Test script to verify automatic camera resolution detection.
Run this to test which cameras are available and what resolutions they support.
"""

import sys
import os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from utils.camera import detect_optimal_camera_resolution, CameraManager

def test_camera_resolution_detection():
    """Test camera resolution detection for available cameras."""
    print("=== Testing Camera Resolution Detection ===\n")
    
    # Test cameras 0-5
    for camera_index in range(6):
        print(f"Testing Camera {camera_index}:")
        print("-" * 30)
        
        # Test detection function
        result = detect_optimal_camera_resolution(camera_index, 640, 480)
        if result:
            width, height, fps = result
            print(f"✓ Direct detection: {width}x{height} @ {fps}fps")
            
            # Test camera manager
            camera_manager = CameraManager(camera_index, 640, 480, 30)
            if camera_manager.start_camera():
                actual_width, actual_height = camera_manager.get_resolution()
                actual_fps = camera_manager.get_fps()
                print(f"✓ CameraManager: {actual_width}x{actual_height} @ {actual_fps}fps")
                camera_manager.stop_camera()
            else:
                print("✗ CameraManager failed to start")
        else:
            print("✗ Camera not available or failed")
        
        print()

def test_specific_cameras():
    """Test the specific cameras mentioned by the user."""
    print("=== Testing Specific Cameras ===\n")
    
    # Test TE-9072 camera (usually /dev/video1)
    print("Testing TE-9072 Camera (Index 1):")
    print("-" * 35)
    result = detect_optimal_camera_resolution(1, 640, 480)
    if result:
        width, height, fps = result
        print(f"TE-9072: {width}x{height} @ {fps}fps")
    else:
        print("TE-9072: Not available")
    
    print()
    
    # Test GC21 Video camera (usually /dev/video3)  
    print("Testing GC21 Video Camera (Index 3):")
    print("-" * 37)
    result = detect_optimal_camera_resolution(3, 640, 480)
    if result:
        width, height, fps = result
        print(f"GC21 Video: {width}x{height} @ {fps}fps")
        
        # Test higher resolution for GC21
        print("\nTesting higher resolutions for GC21:")
        for test_res in [(1280, 720), (1920, 1080), (800, 600)]:
            test_w, test_h = test_res
            result_hd = detect_optimal_camera_resolution(3, test_w, test_h)
            if result_hd:
                actual_w, actual_h, actual_fps = result_hd
                print(f"  {test_w}x{test_h} -> {actual_w}x{actual_h} @ {actual_fps}fps")
    else:
        print("GC21 Video: Not available")

if __name__ == "__main__":
    try:
        test_camera_resolution_detection()
        test_specific_cameras()
        print("=== Test Complete ===")
    except KeyboardInterrupt:
        print("\nTest interrupted by user.")
    except Exception as e:
        print(f"\nError during test: {e}")
        import traceback
        traceback.print_exc() 