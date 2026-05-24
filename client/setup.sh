#!/bin/bash
# Setup script for LexWolf Client

echo "Setting up LexWolf Client..."

# Check if Python is installed
if ! command -v python3 &> /dev/null
then
    echo "Python 3 is not installed. Please install Python 3.7 or later."
    exit 1
fi

# Check if pip is installed
if ! command -v pip3 &> /dev/null
then
    echo "pip3 is not installed. Please install pip3."
    exit 1
fi

# Install required packages
echo "Installing required packages..."
pip3 install -r requirements.txt

# Download German language model for spaCy
echo "Downloading German language model for spaCy..."
python3 -m spacy download de_core_news_sm

# Create data directory
echo "Creating data directory..."
mkdir -p data

echo "Setup complete!"
echo ""
echo "To run the LexWolf client:"
echo "  python3 lexwolf_client.py"