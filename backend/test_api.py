import requests
import json

# Test the API endpoints
BASE_URL = "http://localhost:8000"

def test_documents_endpoint():
    print("Testing /documents endpoint...")
    try:
        response = requests.get(f"{BASE_URL}/documents")
        print(f"Status Code: {response.status_code}")
        if response.status_code == 200:
            print("Success: Documents endpoint works correctly")
            print(f"Response: {response.json()}")
        else:
            print(f"Error: {response.text}")
    except Exception as e:
        print(f"Exception occurred: {e}")

def test_knowledge_endpoint():
    print("\nTesting /knowledge endpoint...")
    try:
        response = requests.get(f"{BASE_URL}/knowledge")
        print(f"Status Code: {response.status_code}")
        if response.status_code == 200:
            print("Success: Knowledge endpoint works correctly")
            print(f"Response: {response.json()}")
        else:
            print(f"Error: {response.text}")
    except Exception as e:
        print(f"Exception occurred: {e}")

def test_knowledge_search():
    print("\nTesting /knowledge/search endpoint...")
    try:
        response = requests.get(f"{BASE_URL}/knowledge/search?query=test")
        print(f"Status Code: {response.status_code}")
        if response.status_code == 200:
            print("Success: Knowledge search endpoint works correctly")
            print(f"Response: {response.json()}")
        else:
            print(f"Error: {response.text}")
    except Exception as e:
        print(f"Exception occurred: {e}")

def test_create_document():
    print("\nTesting POST /documents endpoint...")
    try:
        document_data = {
            "title": "Test Document",
            "content": "This is a test document",
            "document_type": "test"
        }
        response = requests.post(f"{BASE_URL}/documents", json=document_data)
        print(f"Status Code: {response.status_code}")
        if response.status_code == 200:
            print("Success: Document creation works correctly")
            print(f"Response: {response.json()}")
        else:
            print(f"Error: {response.text}")
    except Exception as e:
        print(f"Exception occurred: {e}")

if __name__ == "__main__":
    test_documents_endpoint()
    test_knowledge_endpoint()
    test_knowledge_search()
    test_create_document()