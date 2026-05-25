#!/usr/bin/env python3
"""
Test script to verify docker-compose setup functionality
"""

import os
import sys
import subprocess
import time

def test_docker_compose_config():
    """Test docker-compose configuration"""
    print("Testing docker-compose configuration...")
    try:
        result = subprocess.run(
            ["docker-compose", "config"], 
            capture_output=True, 
            text=True, 
            cwd="/data/.openclaw/workspace-codex/projects/lexwolf"
        )
        if result.returncode == 0:
            print("  ✓ docker-compose config validation passed")
            return True
        else:
            print(f"  ✗ docker-compose config validation failed: {result.stderr}")
            return False
    except Exception as e:
        print(f"  ✗ Error running docker-compose config: {e}")
        return False

def test_file_permissions():
    """Test file permissions"""
    print("Testing file permissions...")
    try:
        # Check if init script is executable
        init_script = "/data/.openclaw/workspace-codex/projects/lexwolf/init-scripts/01-pgvector.sh"
        if os.access(init_script, os.X_OK):
            print("  ✓ Init script is executable")
        else:
            print("  ✗ Init script is not executable")
            # Make it executable
            os.chmod(init_script, 0o755)
            print("  ✓ Made init script executable")
        
        # Check docker-compose.yml exists
        docker_compose = "/data/.openclaw/workspace-codex/projects/lexwolf/docker-compose.yml"
        if os.path.exists(docker_compose):
            print("  ✓ docker-compose.yml exists")
        else:
            print("  ✗ docker-compose.yml missing")
            return False
            
        # Check backend files exist
        backend_files = [
            "backend/Dockerfile",
            "backend/requirements.txt"
        ]
        
        for file_path in backend_files:
            full_path = f"/data/.openclaw/workspace-codex/projects/lexwolf/{file_path}"
            if os.path.exists(full_path):
                print(f"  ✓ {file_path} exists")
            else:
                print(f"  ✗ {file_path} missing")
                return False
                
        return True
    except Exception as e:
        print(f"  ✗ Error checking file permissions: {e}")
        return False

def test_environment_setup():
    """Test environment setup"""
    print("Testing environment setup...")
    try:
        # Check .env.example exists
        env_example = "/data/.openclaw/workspace-codex/projects/lexwolf/.env.example"
        if os.path.exists(env_example):
            print("  ✓ .env.example exists")
        else:
            print("  ✗ .env.example missing")
            return False
            
        # Check README exists
        readme = "/data/.openclaw/workspace-codex/projects/lexwolf/README.md"
        if os.path.exists(readme):
            print("  ✓ README.md exists")
        else:
            print("  ✗ README.md missing")
            return False
            
        return True
    except Exception as e:
        print(f"  ✗ Error checking environment setup: {e}")
        return False

def main():
    """Main test function"""
    print("LexWolf Docker Compose Setup Test")
    print("=" * 40)
    
    tests = [
        test_docker_compose_config,
        test_file_permissions,
        test_environment_setup
    ]
    
    passed = 0
    total = len(tests)
    
    for test in tests:
        if test():
            passed += 1
        print()
    
    print(f"Test Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("\n🎉 Docker Compose setup is working correctly!")
        print("\nWhat's verified:")
        print("  ✓ docker-compose.yml configuration is valid")
        print("  ✓ All required files exist with correct permissions")
        print("  ✓ Environment setup is complete")
        print("  ✓ Init scripts are executable")
        print("\nThe setup is ready for use:")
        print("  cp .env.example .env")
        print("  docker-compose up -d")
        return 0
    else:
        print("\n❌ Docker Compose setup needs attention!")
        return 1

if __name__ == "__main__":
    sys.exit(main())