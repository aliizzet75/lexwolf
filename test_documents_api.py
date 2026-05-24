#!/usr/bin/env python3
"""
Test script for LexWolf Document Generation API
"""

import requests
import json

def test_documents_api():
    """Test document generation API endpoints"""
    base_url = "http://localhost:8000"
    
    print("Testing LexWolf Document Generation API")
    print("=" * 50)
    
    # Test 1: List templates
    print("1. Testing template listing...")
    try:
        response = requests.get(f"{base_url}/documents/")
        if response.status_code == 200:
            templates = response.json()
            print(f"  ✓ Successfully retrieved {len(templates['templates'])} templates")
            for template in templates['templates']:
                print(f"    - {template['title']} ({template['name']})")
        else:
            print(f"  ✗ Failed to list templates: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error listing templates: {e}")
        return False
    
    # Test 2: Get template info
    print("\n2. Testing template info retrieval...")
    try:
        response = requests.get(f"{base_url}/documents/kündigungsschutzklage")
        if response.status_code == 200:
            template_info = response.json()
            print(f"  ✓ Successfully retrieved template info for {template_info['title']}")
            print(f"    Sections: {', '.join(template_info['sections'])}")
        else:
            print(f"  ✗ Failed to get template info: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error getting template info: {e}")
        return False
    
    # Test 3: Generate document
    print("\n3. Testing document generation...")
    try:
        # Sample data for document generation
        data = {
            "court": "Arbeitsgericht Berlin",
            "name": "Max",
            "surname": "Mustermann",
            "address": "Musterstraße 1, 12345 Berlin",
            "opponent_name": "Musterfirma GmbH",
            "opponent_address": "Firmenstraße 2, 12345 Berlin",
            "start_date": "01.01.2020",
            "end_date": "31.12.2023",
            "termination_date": "01.01.2023",
            "position": "Softwareentwickler",
            "court_date": "15.06.2023",
            "court_time": "10:00"
        }
        
        payload = {
            "template_name": "kündigungsschutzklage",
            "data": data,
            "style_profile_id": "sp_a7f3b2"
        }
        
        response = requests.post(f"{base_url}/documents/", json=payload)
        if response.status_code == 200:
            document = response.json()
            print(f"  ✓ Successfully generated document: {document['title']}")
            print(f"    Document ID: {document['document_id']}")
            print(f"    Sections: {len(document['sections'])}")
        else:
            print(f"  ✗ Failed to generate document: {response.status_code}")
            print(f"    Response: {response.text}")
            return False
    except Exception as e:
        print(f"  ✗ Error generating document: {e}")
        return False
    
    # Test 4: Format document
    print("\n4. Testing document formatting...")
    try:
        # Get the document we just generated
        document_response = requests.post(f"{base_url}/documents/", json=payload)
        if document_response.status_code == 200:
            document = document_response.json()
            
            # Test text formatting
            format_response = requests.post(
                f"{base_url}/documents/format", 
                json=document,
                params={"format_type": "text"}
            )
            
            if format_response.status_code == 200:
                formatted = format_response.json()
                print("  ✓ Successfully formatted document as text")
                print(f"    Content preview: {formatted['content'][:100]}...")
            else:
                print(f"  ✗ Failed to format document: {format_response.status_code}")
                return False
        else:
            print(f"  ✗ Failed to get document for formatting: {document_response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error formatting document: {e}")
        return False
    
    # Test 5: Error handling
    print("\n5. Testing error handling...")
    try:
        # Test with invalid template
        payload = {
            "template_name": "invalid_template",
            "data": {},
            "style_profile_id": "sp_a7f3b2"
        }
        
        response = requests.post(f"{base_url}/documents/", json=payload)
        if response.status_code == 404:
            print("  ✓ Error handling works correctly for invalid template")
        else:
            print(f"  ✗ Unexpected response for invalid template: {response.status_code}")
            return False
            
        # Test with missing data
        payload = {
            "template_name": "kündigungsschutzklage",
            "data": {},  # Missing required fields
            "style_profile_id": "sp_a7f3b2"
        }
        
        response = requests.post(f"{base_url}/documents/", json=payload)
        if response.status_code == 400:
            print("  ✓ Error handling works correctly for missing data")
        else:
            print(f"  ✗ Unexpected response for missing data: {response.status_code}")
            return False
            
    except Exception as e:
        print(f"  ✗ Error testing error handling: {e}")
        return False
    
    print("\n" + "=" * 50)
    print("🎉 All document generation API tests passed!")
    return True

def main():
    """Main test function"""
    try:
        success = test_documents_api()
        if success:
            print("\nDocument generation API is working correctly!")
            return 0
        else:
            print("\nDocument generation API tests failed!")
            return 1
    except Exception as e:
        print(f"\nUnexpected error during testing: {e}")
        return 1

if __name__ == "__main__":
    exit(main())