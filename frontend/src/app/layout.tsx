'use client'

import { useState } from 'react'
import Link from 'next/link'
import { BookOpen, Map, Users, Settings } from 'lucide-react'

export default function Layout({ children }) {
  const [activeTab, setActiveTab] = useState('documentation')
  
  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex">
              <div className="flex-shrink-0 flex items-center">
                <BookOpen className="h-8 w-8 text-blue-600" />
                <span className="ml-2 text-xl font-bold text-gray-900">LexWolf</span>
              </div>
              <nav className="ml-6 flex space-x-8">
                <Link 
                  href="/" 
                  className={`inline-flex items-center px-1 pt-1 border-b-2 text-sm font-medium ${
                    activeTab === 'documentation' 
                      ? 'border-blue-500 text-gray-900' 
                      : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                  }`}
                  onClick={() => setActiveTab('documentation')}
                >
                  Documentation
                </Link>
                <Link 
                  href="/roadmap" 
                  className={`inline-flex items-center px-1 pt-1 border-b-2 text-sm font-medium ${
                    activeTab === 'roadmap' 
                      ? 'border-blue-500 text-gray-900' 
                      : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
                  }`}
                  onClick={() => setActiveTab('roadmap')}
                >
                  Roadmap
                </Link>
              </nav>
            </div>
          </div>
        </div>
      </header>
      
      <main>
        {children}
      </main>
    </div>
  )
}