#!/usr/bin/env python3
"""
LexWolf Client Application
==========================

A local client application for German lawyers to work with the LexWolf AI system.

Features:
- Local document processing and style analysis
- Conversation recording and analysis
- Anonymization of sensitive data
- Learning assistant that adapts to user preferences
- Integration with LexWolf server for legal database access

Requirements:
- Python 3.7+
- tkinter (usually included with Python)
- spacy (for NLP processing)
- requests (for server communication)

Installation:
1. pip install -r requirements.txt
2. python -m spacy download de_core_news_sm

Usage:
python lexwolf_client.py
"""

import sys
import os

# Add src directory to Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'src'))

def check_requirements():
    """
    Check if all required packages are installed
    """
    required_packages = [
        'tkinter',
        'spacy',
        'requests',
        'numpy'
    ]
    
    missing_packages = []
    
    # Check tkinter (part of standard library, but might be missing in some installations)
    try:
        import tkinter
    except ImportError:
        missing_packages.append('tkinter')
    
    # Check spacy
    try:
        import spacy
    except ImportError:
        missing_packages.append('spacy')
    
    # Check requests
    try:
        import requests
    except ImportError:
        missing_packages.append('requests')
    
    # Check numpy
    try:
        import numpy
    except ImportError:
        missing_packages.append('numpy')
    
    if missing_packages:
        print("Missing required packages:")
        for package in missing_packages:
            print(f"  - {package}")
        
        print("\nPlease install missing packages with:")
        print("  pip install -r requirements.txt")
        
        # For spacy, also need to download German model
        if 'spacy' in missing_packages or 'spacy' not in missing_packages:
            print("\nAlso install German language model:")
            print("  python -m spacy download de_core_news_sm")
        
        return False
    
    return True

def main():
    """
    Main entry point for the LexWolf client application
    """
    print("LexWolf Client Application")
    print("=" * 30)
    
    # Check requirements
    if not check_requirements():
        sys.exit(1)
    
    # Import and run main application
    try:
        from src.main import main as run_app
        print("Starting LexWolf client...")
        run_app()
    except Exception as e:
        print(f"Error starting application: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()