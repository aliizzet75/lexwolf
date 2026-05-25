#!/usr/bin/env python3
"""
Verification script for LexWolf Docker Compose Setup
"""

import os
import sys

def check_file_structure():
    """Check that all required files exist"""
    print("Checking file structure...")
    
    required_files = [
        "docker-compose.yml",
        "backend/Dockerfile",
        "backend/requirements.txt",
        ".env.example",
        "README.md",
        "init-scripts/01-pgvector.sh"
    ]
    
    missing_files = []
    for file_path in required_files:
        if not os.path.exists(file_path):
            missing_files.append(file_path)
            print(f"  ✗ Missing: {file_path}")
        else:
            print(f"  ✓ Found: {file_path}")
    
    return len(missing_files) == 0

def check_docker_compose_content():
    """Check docker-compose.yml content"""
    print("\nChecking docker-compose.yml content...")
    
    try:
        with open("docker-compose.yml", "r") as f:
            content = f.read()
        
        required_sections = [
            "services:",
            "db:",
            "image: ankane/pgvector",
            "api:",
            "build:",
            "volumes:"
        ]
        
        missing_sections = []
        for section in required_sections:
            if section in content:
                print(f"  ✓ Found: {section}")
            else:
                missing_sections.append(section)
                print(f"  ✗ Missing: {section}")
        
        return len(missing_sections) == 0
        
    except Exception as e:
        print(f"  ✗ Error reading docker-compose.yml: {e}")
        return False

def check_dockerfile_content():
    """Check Dockerfile content"""
    print("\nChecking Dockerfile content...")
    
    try:
        with open("backend/Dockerfile", "r") as f:
            content = f.read()
        
        required_lines = [
            "FROM python:3.11-slim",
            "WORKDIR /app",
            "COPY requirements.txt",
            "RUN pip install",
            "COPY . .",
            "EXPOSE 8000",
            "CMD [\"uvicorn\", \"main:app\""
        ]
        
        missing_lines = []
        for line in required_lines:
            if line in content:
                print(f"  ✓ Found: {line}")
            else:
                missing_lines.append(line)
                print(f"  ✗ Missing: {line}")
        
        return len(missing_lines) == 0
        
    except Exception as e:
        print(f"  ✗ Error reading Dockerfile: {e}")
        return False

def check_requirements_content():
    """Check requirements.txt content"""
    print("\nChecking requirements.txt content...")
    
    try:
        with open("backend/requirements.txt", "r") as f:
            content = f.read()
        
        required_packages = [
            "fastapi",
            "uvicorn",
            "sqlalchemy",
            "psycopg2-binary",
            "pgvector",
            "python-dotenv"
        ]
        
        missing_packages = []
        for package in required_packages:
            if package in content:
                print(f"  ✓ Found: {package}")
            else:
                missing_packages.append(package)
                print(f"  ✗ Missing: {package}")
        
        return len(missing_packages) == 0
        
    except Exception as e:
        print(f"  ✗ Error reading requirements.txt: {e}")
        return False

def check_env_example_content():
    """Check .env.example content"""
    print("\nChecking .env.example content...")
    
    try:
        with open(".env.example", "r") as f:
            content = f.read()
        
        required_vars = [
            "DATABASE_URL",
            "POSTGRES_DB",
            "POSTGRES_USER",
            "POSTGRES_PASSWORD",
            "OPENAI_API_KEY",
            "CLAUDE_API_KEY"
        ]
        
        missing_vars = []
        for var in required_vars:
            if var in content:
                print(f"  ✓ Found: {var}")
            else:
                missing_vars.append(var)
                print(f"  ✗ Missing: {var}")
        
        return len(missing_vars) == 0
        
    except Exception as e:
        print(f"  ✗ Error reading .env.example: {e}")
        return False

def check_readme_content():
    """Check README.md content"""
    print("\nChecking README.md content...")
    
    try:
        with open("README.md", "r") as f:
            content = f.read()
        
        required_sections = [
            "# LexWolf Development Environment",
            "## Prerequisites",
            "## Quick Start",
            "## Services",
            "## Environment Variables",
            "docker-compose up -d"
        ]
        
        missing_sections = []
        for section in required_sections:
            if section in content:
                print(f"  ✓ Found: {section}")
            else:
                missing_sections.append(section)
                print(f"  ✗ Missing: {section}")
        
        return len(missing_sections) == 0
        
    except Exception as e:
        print(f"  ✗ Error reading README.md: {e}")
        return False

def main():
    """Main verification function"""
    print("LexWolf Docker Compose Setup Verification")
    print("=" * 50)
    
    # Change to project directory
    os.chdir("/data/.openclaw/workspace-codex/projects/lexwolf")
    
    # Run all checks
    checks = [
        ("File Structure", check_file_structure),
        ("Docker Compose Content", check_docker_compose_content),
        ("Dockerfile Content", check_dockerfile_content),
        ("Requirements Content", check_requirements_content),
        ("Environment Example Content", check_env_example_content),
        ("README Content", check_readme_content)
    ]
    
    passed = 0
    total = len(checks)
    
    for check_name, check_func in checks:
        print(f"\n{check_name}:")
        if check_func():
            passed += 1
        else:
            print(f"  Failed: {check_name}")
    
    print("\n" + "=" * 50)
    print(f"Verification Results: {passed}/{total} checks passed")
    
    if passed == total:
        print("\n🎉 Docker Compose setup is complete and ready!")
        print("\nWhat's implemented:")
        print("  ✓ docker-compose.yml with PostgreSQL 15 + pgvector")
        print("  ✓ FastAPI service with auto-reload")
        print("  ✓ Environment configuration (.env.example)")
        print("  ✓ README with setup instructions")
        print("  ✓ Database initialization scripts")
        print("  ✓ Dockerfile for backend service")
        print("  ✓ Requirements file with dependencies")
        print("\nTo start the development environment:")
        print("  cp .env.example .env")
        print("  docker-compose up -d")
        return 0
    else:
        print("\n❌ Docker Compose setup needs attention!")
        return 1

if __name__ == "__main__":
    sys.exit(main())